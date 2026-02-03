/**
 * Request building utilities
 */

import { CONFIG } from '../config/constants.js';

/**
 * Resolve header value - supports template variables
 *
 * @param {string} value - Header value (may contain ${VAR} templates)
 * @param {Request} request - Original request
 * @returns {string} - Resolved header value
 */
function resolveHeaderValue(value, request) {
  if (typeof value !== 'string') {
    return String(value);
  }

  // Replace template variables
  return value.replace(/\$\{([^}]+)\}/g, (match, varName) => {
    switch (varName) {
      case 'CF-Connecting-IP':
        return request.headers.get('CF-Connecting-IP') || '';
      case 'CF-IPCountry':
        return request.headers.get('CF-IPCountry') || '';
      case 'CF-Ray':
        return request.headers.get('CF-Ray') || '';
      case 'User-Agent':
        return request.headers.get('User-Agent') || CONFIG.defaultUserAgent;
      default:
        // Try to get from request headers
        return request.headers.get(varName) || match;
    }
  });
}

/**
 * Build fetch options for proxied request
 *
 * @param {Request} request - The original request
 * @param {Object} route - Route configuration
 * @returns {Promise<Object>} - Fetch options object
 */
export async function buildFetchOptions(request, route) {
  const options = {
    method: request.method,
    headers: {},
  };

  // Forward allowed headers from original request (but skip ones that will be overridden)
  const headersToSkip = route.headers ? Object.keys(route.headers).map((k) => k.toLowerCase()) : [];
  const filteredForwardHeaders = CONFIG.forwardHeaders.filter(
    (h) => !headersToSkip.includes(h.toLowerCase())
  );
  forwardHeaders(request, options.headers, filteredForwardHeaders);

  // Apply route-specific headers (these override forwarded headers)
  if (route.headers) {
    for (const [key, value] of Object.entries(route.headers)) {
      const resolvedValue = resolveHeaderValue(value, request);
      if (resolvedValue) {
        options.headers[key] = resolvedValue;
      }
    }
  }

  // Set default user agent if not already set
  if (!options.headers['User-Agent']) {
    options.headers['User-Agent'] = CONFIG.defaultUserAgent;
  }

  // Handle request body for methods that support it
  if (CONFIG.methodsWithBody.includes(request.method)) {
    if (request.body !== null && request.bodyUsed === false) {
      try {
        const clonedRequest = request.clone();
        options.body = await getRequestBody(clonedRequest);
      } catch (error) {
        // If body reading fails, try to use body directly as fallback
        if (request.body) {
          options.body = request.body;
        }
      }
    } else if (request.body !== null) {
      options.body = request.body;
    }
  }

  // Set timeout for origin-specific types
  if (route.originType === 'tailscale' || route.originType === 'home-lab') {
    // Longer timeout for internal services
    options.signal = AbortSignal.timeout(CONFIG.timeout.read);
  }

  return options;
}

/**
 * Forward allowed headers from original request
 *
 * @param {Request} request - The original request
 * @param {Object} targetHeaders - The target headers object
 * @param {string[]} allowedHeaders - List of headers to forward
 */
function forwardHeaders(request, targetHeaders, allowedHeaders) {
  const requestHeaders = request.headers;
  for (const headerName of allowedHeaders) {
    const headerValue = requestHeaders.get(headerName);
    if (headerValue) {
      targetHeaders[headerName] = headerValue;
    }
  }
}

/**
 * Extract request body based on content type
 *
 * @param {Request} request - The original request
 * @returns {Promise<string|FormData|Blob>} - The request body
 */
async function getRequestBody(request) {
  const contentType = (request.headers.get('content-type') || '').toLowerCase();

  if (contentType.includes('application/json')) {
    const json = await request.json();
    return JSON.stringify(json);
  }

  if (contentType.includes('application/text') || contentType.includes('text/html')) {
    return await request.text();
  }

  if (contentType.includes('form')) {
    return await request.formData();
  }

  // Default: return as blob
  return await request.blob();
}

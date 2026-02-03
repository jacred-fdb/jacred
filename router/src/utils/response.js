/**
 * Response handling utilities
 */

import { CONFIG } from '../config/constants.js';

/**
 * Apply caching headers to response headers
 *
 * @param {Headers} headers - The headers object
 * @param {Object} cacheConfig - Cache configuration from route
 * @returns {void}
 */
export function applyCacheHeaders(headers, cacheConfig) {
  const targetHeaders = headers;

  if (!cacheConfig || !cacheConfig.enabled) {
    // No caching
    targetHeaders.set('Cache-Control', 'no-cache, no-store, must-revalidate');
    targetHeaders.set('Pragma', 'no-cache');
    targetHeaders.set('Expires', '0');
    return;
  }

  // Build Cache-Control header
  const cacheControl = [];
  cacheControl.push('public');
  cacheControl.push(`max-age=${cacheConfig.ttl || 0}`);

  if (cacheConfig.staleWhileRevalidate) {
    cacheControl.push(`stale-while-revalidate=${cacheConfig.staleWhileRevalidate}`);
  }

  if (cacheConfig.staleIfError) {
    cacheControl.push(`stale-if-error=${cacheConfig.staleIfError}`);
  }

  targetHeaders.set('Cache-Control', cacheControl.join(', '));

  // Set Vary header if specified
  if (cacheConfig.vary && cacheConfig.vary.length > 0) {
    const existingVary = targetHeaders.get('Vary');
    const varyHeaders = existingVary ? [...cacheConfig.vary, existingVary] : cacheConfig.vary;
    targetHeaders.set('Vary', varyHeaders.join(', '));
  }

  // Set Expires header based on TTL
  if (cacheConfig.ttl) {
    const expiresDate = new Date(Date.now() + cacheConfig.ttl * 1000);
    targetHeaders.set('Expires', expiresDate.toUTCString());
  }
}

/**
 * Copy relevant headers from origin response
 *
 * @param {Headers} originHeaders - Headers from origin response
 * @param {Headers} targetHeaders - Target headers object
 * @param {string[]} excludeHeaders - Headers to exclude
 */
export function copyResponseHeaders(originHeaders, targetHeaders, excludeHeaders = []) {
  const defaultExclude = [
    'connection',
    'transfer-encoding',
    'content-encoding',
    'content-length', // Will be recalculated
    'cf-ray',
    'cf-request-id',
    'server',
    'x-powered-by',
    ...excludeHeaders,
  ];

  for (const [key, value] of originHeaders.entries()) {
    const lowerKey = key.toLowerCase();
    if (!defaultExclude.includes(lowerKey)) {
      targetHeaders.set(key, value);
    }
  }
}

/**
 * Create error response
 *
 * @param {string} message - Error message
 * @param {number} status - HTTP status code
 * @returns {Response} - Error response
 */
export function createErrorResponse(message, status = CONFIG.statusCodes.INTERNAL_SERVER_ERROR) {
  const body = JSON.stringify({
    error: true,
    message,
    status,
  });

  return new Response(body, {
    status,
    headers: {
      'Content-Type': 'application/json',
      'Cache-Control': 'no-cache, no-store, must-revalidate',
    },
  });
}

/**
 * Check if status code indicates a redirect
 *
 * @param {number} status - HTTP status code
 * @returns {boolean} - True if redirect status
 */
export function isRedirectStatus(status) {
  return status >= 300 && status < 400;
}

/**
 * Handle redirect response from origin
 *
 * @param {Response} originResponse - Response from origin
 * @param {string} targetUrl - Target URL that was requested
 * @returns {Response} - Redirect response
 */
export function handleRedirect(originResponse, targetUrl) {
  const location = originResponse.headers.get('Location');
  if (!location) {
    return originResponse;
  }

  // If location is relative, make it absolute
  let redirectUrl = location;
  try {
    new URL(location);
  } catch {
    // Relative URL, make it absolute
    const targetUrlObj = new URL(targetUrl);
    redirectUrl = new URL(location, targetUrlObj.origin + targetUrlObj.pathname).href;
  }

  // Create redirect response
  return new Response(null, {
    status: originResponse.status,
    statusText: originResponse.statusText,
    headers: {
      Location: redirectUrl,
      'Cache-Control': 'no-cache, no-store, must-revalidate',
    },
  });
}

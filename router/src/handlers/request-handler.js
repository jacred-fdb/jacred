/**
 * Main request handler for the Router worker
 */

import { findMatchingRoute, buildTargetUrl } from '../utils/route-matcher.js';
import { buildFetchOptions } from '../utils/request-builder.js';
import {
  applyCacheHeaders,
  copyResponseHeaders,
  createErrorResponse,
  handleRedirect,
  isRedirectStatus,
} from '../utils/response.js';
import { CONFIG } from '../config/constants.js';

/**
 * Handle incoming request
 *
 * @param {Request} request - The incoming request
 * @param {Object} env - Environment variables
 * @param {ExecutionContext} ctx - Execution context
 * @returns {Promise<Response>} - The response
 */
export async function handleRequest(request, env, ctx) {
  // Handle OPTIONS preflight requests
  if (request.method === 'OPTIONS') {
    return new Response(null, {
      status: CONFIG.statusCodes.NO_CONTENT,
      headers: {
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Methods': 'GET, POST, PUT, PATCH, DELETE, OPTIONS',
        'Access-Control-Allow-Headers': '*',
        'Access-Control-Max-Age': '86400',
      },
    });
  }

  try {
    const url = new URL(request.url);
    const pathname = url.pathname;
    const search = url.search;
    const queryParams = url.searchParams;

    // Determine hostname: query param override (for local testing) > Host header > URL hostname
    const hostOverride = queryParams.get('__host');
    const hostHeader = request.headers.get('Host');
    const urlHostname = url.hostname;

    const hostname = hostOverride || hostHeader?.split(':')[0] || urlHostname;

    // Find matching route
    const route = findMatchingRoute(hostname, pathname, queryParams);

    if (!route) {
      return createErrorResponse('No route matched', CONFIG.statusCodes.NOT_FOUND);
    }

    // Build target URL
    let targetUrl;
    try {
      targetUrl = buildTargetUrl(route, pathname, search);
    } catch (error) {
      console.error('Error building target URL:', error);
      return createErrorResponse(
        'Invalid route configuration',
        CONFIG.statusCodes.INTERNAL_SERVER_ERROR
      );
    }

    // Build fetch options
    let fetchOptions;
    try {
      fetchOptions = await buildFetchOptions(request, route);
    } catch (error) {
      console.error('Error building fetch options:', error);
      return createErrorResponse(
        'Error preparing request',
        CONFIG.statusCodes.INTERNAL_SERVER_ERROR
      );
    }

    // Make the proxied request
    let originResponse;
    try {
      originResponse = await fetch(targetUrl, fetchOptions);
    } catch (error) {
      console.error('Error fetching from origin:', error);
      const status =
        error.name === 'AbortError' || error.name === 'TimeoutError'
          ? CONFIG.statusCodes.GATEWAY_TIMEOUT
          : CONFIG.statusCodes.BAD_GATEWAY;
      return createErrorResponse(`Error connecting to origin: ${error.message}`, status);
    }

    // Handle redirects
    if (isRedirectStatus(originResponse.status)) {
      return handleRedirect(originResponse, targetUrl);
    }

    // Create response with headers from origin
    const responseHeaders = new Headers();
    copyResponseHeaders(originResponse.headers, responseHeaders);

    // Apply cache headers based on route configuration
    const cacheConfig = route.cache || CONFIG.defaultCache;
    applyCacheHeaders(responseHeaders, cacheConfig);

    // Use Cloudflare Cache API if caching is enabled
    if (cacheConfig.enabled && request.method === 'GET') {
      // Create cache key
      const cacheKey = new Request(targetUrl, {
        method: 'GET',
        headers: request.headers,
      });

      // Store in cache (non-blocking)
      ctx.waitUntil(caches.default.put(cacheKey, originResponse.clone()).catch(() => {}));
    }

    // Return response with body stream
    return new Response(originResponse.body, {
      status: originResponse.status,
      statusText: originResponse.statusText,
      headers: responseHeaders,
    });
  } catch (error) {
    console.error('Request handler error:', error);
    return createErrorResponse(
      error.message || 'Internal server error',
      CONFIG.statusCodes.INTERNAL_SERVER_ERROR
    );
  }
}

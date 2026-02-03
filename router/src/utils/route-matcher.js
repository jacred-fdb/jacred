/**
 * Route matching utilities
 */

import { ROUTES } from '../config/constants.js';

/**
 * Convert path pattern to regex
 * Supports wildcards: * matches any characters, ** matches any path segments
 *
 * @param {string} pattern - Path pattern (e.g., '/api/*', '/static/**')
 * @returns {RegExp} - Regular expression for matching
 */
function pathPatternToRegex(pattern) {
  if (!pattern || pattern === '/*' || pattern === '*') {
    return /^.*$/;
  }

  // Escape special regex characters except * and /
  let regexPattern = pattern
    .replace(/[.+?^${}()|[\]\\]/g, '\\$&')
    // Convert ** to match any path segments
    .replace(/\*\*/g, '.*')
    // Convert * to match any characters except /
    .replace(/\*/g, '[^/]*');

  // Ensure pattern matches from start
  if (!regexPattern.startsWith('^')) {
    regexPattern = '^' + regexPattern;
  }

  // Ensure pattern matches to end
  if (!regexPattern.endsWith('$')) {
    regexPattern = regexPattern + '$';
  }

  return new RegExp(regexPattern);
}

/**
 * Match query parameters
 *
 * @param {URLSearchParams} queryParams - Request query parameters
 * @param {Object} routeQuery - Route query requirements
 * @returns {boolean} - True if query matches
 */
function matchQuery(queryParams, routeQuery) {
  if (!routeQuery || Object.keys(routeQuery).length === 0) {
    return true;
  }

  for (const [key, value] of Object.entries(routeQuery)) {
    const paramValue = queryParams.get(key);
    if (value === '*') {
      // Wildcard: parameter must exist
      if (!paramValue) {
        return false;
      }
    } else if (value instanceof RegExp) {
      // Regex match
      if (!paramValue || !value.test(paramValue)) {
        return false;
      }
    } else if (typeof value === 'string') {
      // Exact match
      if (paramValue !== value) {
        return false;
      }
    } else if (Array.isArray(value)) {
      // One of multiple values
      if (!value.includes(paramValue)) {
        return false;
      }
    }
  }

  return true;
}

/**
 * Find matching route for request
 *
 * @param {string} hostname - Request hostname
 * @param {string} pathname - Request pathname
 * @param {URLSearchParams} queryParams - Request query parameters
 * @returns {Object|null} - Matching route configuration or null
 */
export function findMatchingRoute(hostname, pathname, queryParams) {
  // Normalize hostname to lowercase for case-insensitive matching
  const normalizedHostname = hostname?.toLowerCase() || '';

  for (const route of ROUTES) {
    // Match hostname (exact match or wildcard)
    if (route.hostname) {
      const routeHostname = route.hostname.toLowerCase();

      if (routeHostname !== normalizedHostname && routeHostname !== '*') {
        // Support wildcard subdomain matching (e.g., *.example.com)
        if (routeHostname.startsWith('*.')) {
          const domain = routeHostname.substring(2);
          if (!normalizedHostname.endsWith('.' + domain) && normalizedHostname !== domain) {
            continue;
          }
        } else {
          continue;
        }
      }
    }

    // Match path
    if (route.path) {
      const pathRegex = pathPatternToRegex(route.path);
      if (!pathRegex.test(pathname)) {
        continue;
      }
    }

    // Match query parameters
    if (route.query && !matchQuery(queryParams, route.query)) {
      continue;
    }

    // Route matches!
    return route;
  }

  return null;
}

/**
 * Build target URL from route and request
 *
 * @param {Object} route - Route configuration
 * @param {string} pathname - Request pathname
 * @param {string} search - Request query string
 * @returns {string} - Target URL
 */
export function buildTargetUrl(route, pathname, search) {
  const origin = route.origin;
  if (!origin) {
    throw new Error(`Route ${route.name} has no origin configured`);
  }

  // Remove trailing slash from origin
  const cleanOrigin = origin.replace(/\/$/, '');

  // If route has pathRewrite, apply it
  if (route.pathRewrite) {
    if (typeof route.pathRewrite === 'function') {
      pathname = route.pathRewrite(pathname);
    } else if (typeof route.pathRewrite === 'object' && route.pathRewrite.pattern) {
      const regex = new RegExp(route.pathRewrite.pattern);
      pathname = pathname.replace(regex, route.pathRewrite.replacement || '');
    }
  }

  return `${cleanOrigin}${pathname}${search || ''}`;
}

/**
 * Router Worker
 * A Cloudflare Worker that acts as a smart reverse proxy/router,
 * routing requests by hostname/path/query to different origins
 * (home lab, Tailscale, tunnels, Pages, Vercel) with per-route
 * headers and caching policies.
 */

import { handleRequest } from './handlers/request-handler.js';

/**
 * Cloudflare Worker export
 */
export default {
  async fetch(request, env, ctx) {
    return await handleRequest(request, env, ctx);
  },
};

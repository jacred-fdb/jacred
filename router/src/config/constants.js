/**
 * Configuration constants for the Router worker
 */

/**
 * Route configuration
 * Each route can match by hostname, path, and/or query parameters
 * Routes are evaluated in order - first match wins
 */
export const ROUTES = [
  // Example routes - customize these for your needs
  // {
  //   name: 'home-lab-api',
  //   hostname: 'api.example.com',
  //   path: '/api/*',
  //   origin: 'https://home-lab.example.com',
  //   originType: 'home-lab',
  //   headers: {
  //     'X-Forwarded-Host': 'api.example.com',
  //     'X-Real-IP': '${CF-Connecting-IP}',
  //   },
  //   cache: {
  //     enabled: true,
  //     ttl: 300, // 5 minutes
  //     vary: ['Accept', 'Accept-Language'],
  //   },
  // },
  {
    name: 'torrent-by',
    hostname: 'torrent.torrservera.net',
    path: '/*',
    origin: 'https://torrent.by',
    originType: 'tunnel',
    headers: {
      'X-Forwarded-Host': 'torrent.torrservera.net',
      'X-Forwarded-Proto': 'https',
      'X-Real-IP': '${CF-Connecting-IP}',
      'X-Cloudflare-Country': 'BY',
      'CF-IPCountry': 'BY',
    },
    cache: {
      enabled: false,
    },
  },
  {
    name: 'megapeer-vip',
    hostname: 'megapeer.torrservera.net',
    path: '/*',
    origin: 'https://megapeer.vip',
    originType: 'tunnel',
    headers: {
      'User-Agent':
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
      Accept:
        'text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8',
      'Accept-Language': 'en-US,en;q=0.9',
      'Accept-Encoding': 'gzip, deflate, br',
      'X-Forwarded-Host': 'megapeer.torrservera.net',
      'X-Forwarded-Proto': 'https',
      'X-Real-IP': '${CF-Connecting-IP}',
      'X-Cloudflare-Country': 'BY',
      'CF-IPCountry': 'BY',
    },
    cache: {
      enabled: false,
    },
  },
  {
    name: 'bitru-org',
    hostname: 'bitru.torrservera.net',
    path: '/*',
    origin: 'https://bitru.org',
    originType: 'tunnel',
    headers: {
      'User-Agent':
        'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
      Accept:
        'text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8',
      'Accept-Language': 'en-US,en;q=0.9',
      'Accept-Encoding': 'gzip, deflate, br',
      'X-Forwarded-Host': 'bitru.torrservera.net',
      'X-Forwarded-Proto': 'https',
      'X-Real-IP': '${CF-Connecting-IP}',
      'X-Cloudflare-Country': 'BY',
      'CF-IPCountry': 'BY',
    },
    cache: {
      enabled: false,
    },
  },
];

/**
 * Default configuration
 */
export const CONFIG = {
  // Default headers to forward from original request
  forwardHeaders: [
    'accept',
    'accept-encoding',
    'accept-language',
    'content-type',
    'authorization',
    'cookie',
    'user-agent',
  ],

  // Default user agent
  defaultUserAgent:
    'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.0.0 Safari/537.36',

  // Default cache settings
  defaultCache: {
    enabled: false,
    ttl: 0,
    vary: [],
  },

  // HTTP methods that support request bodies
  methodsWithBody: ['POST', 'PUT', 'PATCH', 'DELETE'],

  // Status codes
  statusCodes: {
    OK: 200,
    NO_CONTENT: 204,
    BAD_REQUEST: 400,
    NOT_FOUND: 404,
    INTERNAL_SERVER_ERROR: 500,
    BAD_GATEWAY: 502,
    SERVICE_UNAVAILABLE: 503,
    GATEWAY_TIMEOUT: 504,
  },

  // Timeout settings (in milliseconds)
  timeout: {
    connect: 10000, // 10 seconds
    read: 30000, // 30 seconds
  },
};

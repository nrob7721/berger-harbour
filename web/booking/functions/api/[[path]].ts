// Proxies same-origin /api/* to the public-api Cloud Run service, adding the shared edge secret.
// Pages project environment variables: API_ORIGIN (the Cloud Run URL) and EDGE_PROXY_SECRET (encrypted).

interface Env {
  API_ORIGIN: string;
  EDGE_PROXY_SECRET: string;
}

const FORWARDED_HEADERS = ['content-type', 'accept', 'accept-language', 'user-agent'];

export const onRequest: PagesFunction<Env> = async ({ request, env }) => {
  const url = new URL(request.url);
  const target = new URL(url.pathname + url.search, env.API_ORIGIN);
  const headers = new Headers();
  for (const name of FORWARDED_HEADERS) {
    const value = request.headers.get(name);
    if (value) headers.set(name, value);
  }
  headers.set('X-Edge-Proxy-Secret', env.EDGE_PROXY_SECRET);
  const ip = request.headers.get('CF-Connecting-IP');
  if (ip) headers.set('CF-Connecting-IP', ip);

  const response = await fetch(target.toString(), {
    method: request.method,
    headers,
    body: request.method === 'GET' || request.method === 'HEAD' ? undefined : request.body,
    redirect: 'manual',
  });
  const out = new Response(response.body, response);
  out.headers.set('Cache-Control', 'no-store');
  return out;
};

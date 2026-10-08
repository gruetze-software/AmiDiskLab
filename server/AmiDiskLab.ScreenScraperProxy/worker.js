const API_ROOT = "https://api.screenscraper.fr/api2/";
const ALLOWED_XML_ENDPOINTS = new Set(["jeuInfos.php", "jeuRecherche.php", "ssuserInfos.php"]);
const ALLOWED_MEDIA_ENDPOINTS = new Set(["mediaJeu.php", "mediaCompagnie.php"]);
const ALLOWED_PARAMETER_NAMES = new Set([
  "crc", "md5", "sha1", "romnom", "romtaille", "romtype", "systemeid",
  "recherche", "ssid", "sspassword", "companyid", "jeuId", "gameid",
  "media", "region", "langue", "outputformat", "maxwidth", "maxheight",
  "num", "version", "hd"
]);
const MAX_BODY_BYTES = 16_384;
const MEDIA_TOKEN_LIFETIME_SECONDS = 15 * 60;

export default {
  async fetch(request, env) {
    try {
      const url = new URL(request.url);
      if (request.method === "GET" && url.pathname === "/health")
        return json({ status: "ok", service: "AmiDiskLab ScreenScraper proxy" });

      if (!env.SCREENSCRAPER_DEVELOPER_ID || !env.SCREENSCRAPER_DEVELOPER_PASSWORD ||
          !env.MEDIA_TOKEN_SECRET)
        return json({ error: "Service is not configured." }, 503);

      if (request.method === "POST" && url.pathname === "/v1/screenscraper")
        return await proxyXml(request, env, url.origin);
      if (request.method === "POST" && url.pathname === "/v1/media-token")
        return await createMediaToken(request, env, url.origin);
      if (request.method === "GET" && url.pathname === "/v1/media")
        return await proxyMedia(url, env);
      return json({ error: "Not found." }, 404);
    } catch (error) {
      const stage = error instanceof ProxyStageError ? error.stage : "unexpected";
      console.error("Proxy request failed", { stage, name: error?.name ?? "Error" });
      return json({ error: "Proxy request failed.", stage }, 502);
    }
  }
};

async function proxyXml(request, env, origin) {
  const body = await readJson(request);
  if (!ALLOWED_XML_ENDPOINTS.has(body.endpoint))
    return json({ error: "Endpoint is not allowed." }, 400);
  const upstream = buildUpstreamUrl(body.endpoint, body.parameters, env);
  const response = await atStage("upstream-fetch", () => fetch(upstream, {
    headers: { "User-Agent": "AmiDiskLab-Proxy/0.1" },
    redirect: "follow"
  }));
  if (!response.ok)
    return json({ error: `ScreenScraper returned HTTP ${response.status}.` }, response.status);
  const contentType = response.headers.get("content-type") ?? "application/xml";
  if (!contentType.includes("xml") && !contentType.includes("text"))
    return json({ error: "Unexpected ScreenScraper response." }, 502);
  const xml = await atStage("upstream-read", () => response.text());
  if (xml.length > 4_000_000) return json({ error: "Response is too large." }, 502);
  const protectedXml = await atStage("media-protection", () => protectMediaUrls(xml, env, origin));
  return new Response(protectedXml, {
    status: 200,
    headers: securityHeaders({ "content-type": "application/xml; charset=utf-8" })
  });
}

async function createMediaToken(request, env, origin) {
  const body = await readJson(request);
  if (!ALLOWED_MEDIA_ENDPOINTS.has(body.endpoint))
    return json({ error: "Media endpoint is not allowed." }, 400);
  const upstream = buildUpstreamUrl(body.endpoint, body.parameters, env);
  return json({ url: await encryptedMediaUrl(upstream, env, origin) });
}

async function proxyMedia(url, env) {
  const token = url.searchParams.get("token");
  if (!token || token.length > 8192) return json({ error: "Invalid media token." }, 400);
  const payload = await decryptToken(token, env.MEDIA_TOKEN_SECRET);
  if (!payload || payload.expires < Math.floor(Date.now() / 1000) ||
      !isScreenScraperUrl(payload.url))
    return json({ error: "Media token is invalid or expired." }, 403);
  const response = await fetch(payload.url, {
    headers: { "User-Agent": "AmiDiskLab-Proxy/0.1" },
    redirect: "follow"
  });
  if (!response.ok) return json({ error: `Media download returned HTTP ${response.status}.` }, response.status);
  const contentType = response.headers.get("content-type") ?? "application/octet-stream";
  if (!contentType.startsWith("image/")) return json({ error: "Unexpected media response." }, 502);
  return new Response(response.body, {
    status: 200,
    headers: securityHeaders({
      "content-type": contentType,
      "cache-control": "private, max-age=600",
      "x-content-type-options": "nosniff"
    })
  });
}

async function readJson(request) {
  const declaredLength = Number(request.headers.get("content-length") ?? "0");
  if (declaredLength > MAX_BODY_BYTES) throw new Error("Request body is too large.");
  const text = await request.text();
  if (new TextEncoder().encode(text).length > MAX_BODY_BYTES)
    throw new Error("Request body is too large.");
  const body = JSON.parse(text);
  if (!body || typeof body !== "object" || Array.isArray(body))
    throw new Error("Invalid JSON body.");
  return body;
}

function buildUpstreamUrl(endpoint, parameters, env) {
  if (!parameters || typeof parameters !== "object" || Array.isArray(parameters))
    throw new Error("Parameters are required.");
  const url = new URL(endpoint, API_ROOT);
  url.searchParams.set("devid", env.SCREENSCRAPER_DEVELOPER_ID);
  url.searchParams.set("devpassword", env.SCREENSCRAPER_DEVELOPER_PASSWORD);
  url.searchParams.set("softname", "AmiDiskLab");
  url.searchParams.set("output", "xml");
  for (const [name, rawValue] of Object.entries(parameters)) {
    if (!ALLOWED_PARAMETER_NAMES.has(name) || rawValue === null || rawValue === undefined) continue;
    const value = String(rawValue);
    if (value.length > 1024 || /[\u0000-\u001f]/u.test(value))
      throw new Error("Invalid parameter value.");
    url.searchParams.set(name, value);
  }
  return url.toString();
}

async function protectMediaUrls(xml, env, origin) {
  const pattern = /https:\/\/[A-Za-z0-9.-]*screenscraper\.fr\/[^\s"'<>]+/gi;
  const matches = [...new Set(xml.match(pattern) ?? [])];
  let result = xml;
  for (const encoded of matches) {
    const decoded = encoded.replaceAll("&amp;", "&");
    if (!isScreenScraperUrl(decoded)) continue;
    const replacement = await encryptedMediaUrl(decoded, env, origin);
    result = result.replaceAll(encoded, replacement.replaceAll("&", "&amp;"));
  }
  return result;
}

async function encryptedMediaUrl(upstreamUrl, env, origin) {
  const token = await encryptToken({
    url: upstreamUrl,
    expires: Math.floor(Date.now() / 1000) + MEDIA_TOKEN_LIFETIME_SECONDS
  }, env.MEDIA_TOKEN_SECRET);
  return `${origin}/v1/media?token=${encodeURIComponent(token)}`;
}

function isScreenScraperUrl(value) {
  try {
    const url = new URL(value);
    return url.protocol === "https:" &&
      (url.hostname === "screenscraper.fr" || url.hostname.endsWith(".screenscraper.fr"));
  } catch { return false; }
}

async function encryptionKey(secret, usages) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(secret));
  return crypto.subtle.importKey("raw", digest, { name: "AES-GCM" }, false, usages);
}

async function encryptToken(payload, secret) {
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const key = await encryptionKey(secret, ["encrypt"]);
  const plaintext = new TextEncoder().encode(JSON.stringify(payload));
  const encrypted = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, plaintext));
  const bytes = new Uint8Array(iv.length + encrypted.length);
  bytes.set(iv); bytes.set(encrypted, iv.length);
  return base64Url(bytes);
}

async function decryptToken(token, secret) {
  try {
    const bytes = fromBase64Url(token);
    if (bytes.length < 29) return null;
    const iv = bytes.slice(0, 12);
    const key = await encryptionKey(secret, ["decrypt"]);
    const plaintext = await crypto.subtle.decrypt({ name: "AES-GCM", iv }, key, bytes.slice(12));
    return JSON.parse(new TextDecoder().decode(plaintext));
  } catch { return null; }
}

function base64Url(bytes) {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}

function fromBase64Url(value) {
  const padded = value.replaceAll("-", "+").replaceAll("_", "/") + "===".slice((value.length + 3) % 4);
  const binary = atob(padded);
  return Uint8Array.from(binary, character => character.charCodeAt(0));
}

function securityHeaders(headers = {}) {
  return {
    ...headers,
    "referrer-policy": "no-referrer",
    "x-frame-options": "DENY",
    "permissions-policy": "camera=(), microphone=(), geolocation=()"
  };
}

function json(value, status = 200) {
  return new Response(JSON.stringify(value), {
    status,
    headers: securityHeaders({ "content-type": "application/json; charset=utf-8" })
  });
}

class ProxyStageError extends Error {
  constructor(stage, cause) {
  super(`Proxy stage failed: ${stage}`, { cause });
    this.name = "ProxyStageError";
    this.stage = stage;
  }
}

async function atStage(stage, operation) {
  try { return await operation(); }
  catch (error) { throw new ProxyStageError(stage, error); }
}

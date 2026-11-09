import { getStore } from "@netlify/blobs";
import { handleMailbox } from "../../src/mailbox.mjs";

export default async function handler(request) {
  try {
    // Never enable by deploying alone. An explicit function-scope Netlify
    // environment variable is required for the encrypted mailbox endpoints.
    const enabled = process.env.SCOLIA_NETLIFY_MAILBOX_ENABLED === "true";
    const store = enabled ? getStore({ name: "scolia-ephemeral-v1", consistency: "strong" }) : null;
    return await handleMailbox(request, { enabled, store });
  } catch {
    // Never expose Netlify credentials, blob paths or raw request bodies.
    return Response.json({ error: "Service de secours indisponible." }, {
      status: 503, headers: { "Cache-Control": "no-store" }
    });
  }
}
export const config = { path: "/api/scolia-backup/*" };

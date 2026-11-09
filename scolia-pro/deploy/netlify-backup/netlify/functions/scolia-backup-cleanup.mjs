import { getStore } from "@netlify/blobs";

export default async function cleanup() {
  // Disabled by default: no mailbox or retention processing before an audited
  // SCOLIA client transport adapter is deployed.
  if (process.env.SCOLIA_NETLIFY_MAILBOX_ENABLED !== "true")
    return new Response("Mailbox disabled");
  const store = getStore({ name: "scolia-ephemeral-v1", consistency: "strong" });
  const cutoff = Date.now() - 86_400_000;
  let removed = 0;
  for (const prefix of ["rooms/", "mail/"]) {
    const list = await store.list({ prefix });
    for (const item of (list.blobs || []).slice(0, 200)) {
      if (!item.key.startsWith(prefix)) continue;
      const record = await store.get(item.key, { type: "json", consistency: "strong" });
      if (record && Number(record.expires_at) < cutoff) {
        await store.delete(item.key);
        removed++;
      }
    }
  }
  return new Response("Expired records removed: " + removed);
}
export const config = { schedule: "17 * * * *" };

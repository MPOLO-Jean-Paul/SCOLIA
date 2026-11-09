/**
 * No persisted mailbox records are created during the health-only rollout.
 * Scheduled cleanup therefore remains an explicit no-op.
 * Do not re-enable storage without a validated client protocol and quotas.
 */
export default async function cleanup() {
  return new Response("Mailbox disabled; no stored messages to clean.", {
    headers: { "Cache-Control": "no-store" },
  });
}

export const config = { schedule: "17 * * * *" };

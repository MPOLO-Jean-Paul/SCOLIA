/** Logo du logiciel : un S en deux pièces, marine et vert. Les couleurs suivent le thème (voir --logo-a et --logo-b). */
export function ScoliaMark({ size = 56, className }: { size?: number; className?: string }) {
  return (
    <svg className={className} width={size} height={size} viewBox="40 40 432 432" role="img" aria-label="SCOLIA">
      <g fill="none" strokeWidth={62} strokeLinecap="round" strokeLinejoin="round">
        <path d="M330 118 H206 Q148 118 148 176 Q148 234 206 234 H226" stroke="var(--logo-a)" />
        <path d="M286 278 H306 Q364 278 364 336 Q364 394 306 394 H182" stroke="var(--logo-b)" />
      </g>
      <circle cx="418" cy="118" r="38" fill="var(--logo-a)" />
      <circle cx="94" cy="394" r="38" fill="var(--logo-b)" />
    </svg>
  );
}

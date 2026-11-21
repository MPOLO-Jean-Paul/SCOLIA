// Vérification du format ASP.NET Core IdentityV3 produit par
// Scolia.Infrastructure.Persistence.PasswordService (.NET 10).
// IdentityV3 : 0x01 | prf(4 BE) | iterations(4 BE) | saltLength(4 BE)
//              | salt | subkey. PRF = 2 => HMAC-SHA512.
// Aucun mot de passe ni matériau dérivé n'est journalisé.
const encoder = new TextEncoder();
const limits = Object.freeze({
  minIterations: 100_000,
  maxIterations: 1_000_000,
  minSaltBytes: 16,
  maxSaltBytes: 64,
  minSubkeyBytes: 16,
  maxSubkeyBytes: 64,
});

function decodeIdentityV3(encoded) {
  if (typeof encoded !== "string" || encoded.length < 70 || encoded.length > 260 ||
      !/^[A-Za-z0-9+/]+={0,2}$/.test(encoded)) return null;
  try {
    const input = atob(encoded);
    const bytes = Uint8Array.from(input, char => char.charCodeAt(0));
    if (bytes.length < 13 || bytes[0] !== 0x01) return null;
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const prf = view.getUint32(1, false);
    const iterations = view.getUint32(5, false);
    const saltLength = view.getUint32(9, false);
    const subkeyLength = bytes.length - 13 - saltLength;
    // N'accepter que le schéma réel SCOLIA 1.2.86 (Identity V3/HMAC-SHA512).
    // Legacy V2/SHA1 ou hachages faibles => réinitialisation contrôlée,
    // jamais un downgrade silencieux du service central.
    if (prf !== 2 || iterations < limits.minIterations ||
        iterations > limits.maxIterations ||
        saltLength < limits.minSaltBytes || saltLength > limits.maxSaltBytes ||
        subkeyLength < limits.minSubkeyBytes || subkeyLength > limits.maxSubkeyBytes)
      return null;
    return {
      iterations,
      salt: bytes.slice(13, 13 + saltLength),
      expected: bytes.slice(13 + saltLength),
    };
  } catch {
    return null;
  }
}

function equalConstantTime(left, right) {
  let mismatch = left.length ^ right.length;
  const max = Math.max(left.length, right.length);
  for (let i = 0; i < max; i++)
    mismatch |= (left[i] ?? 0) ^ (right[i] ?? 0);
  return mismatch === 0;
}

async function pbkdf2(password, salt, iterations, bytes) {
  const key = await crypto.subtle.importKey("raw", encoder.encode(password),
    { name: "PBKDF2" }, false, ["deriveBits"]);
  return new Uint8Array(await crypto.subtle.deriveBits({
    name: "PBKDF2", hash: "SHA-512", salt, iterations,
  }, key, bytes * 8));
}

// Uniformiser l'effort en l'absence d'un compte ou avec un hash invalide.
// La valeur attendue n'est pas un compte réel, seulement une comparaison factice.
const dummySalt = new Uint8Array([
  52, 133, 41, 187, 86, 11, 49, 203, 107, 75, 206, 94, 212, 6, 151, 27,
]);
const dummyExpected = new Uint8Array(32);

export async function verifyScoliaPassword(password, storedHash) {
  if (typeof password !== "string" || password.length < 1 ||
      encoder.encode(password).length > 256) return false;
  const parsed = decodeIdentityV3(storedHash);
  const salt = parsed?.salt ?? dummySalt;
  const iterations = parsed?.iterations ?? limits.minIterations;
  const expected = parsed?.expected ?? dummyExpected;
  const derived = await pbkdf2(password, salt, iterations, expected.length);
  return parsed !== null && equalConstantTime(derived, expected);
}

export function passwordHashParameters(storedHash) {
  const parsed = decodeIdentityV3(storedHash);
  return parsed ? { compatible: true, iterations: parsed.iterations } :
    { compatible: false };
}

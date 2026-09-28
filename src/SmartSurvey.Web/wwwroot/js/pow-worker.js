// SmartSurvey bot protection — proof-of-work solver, run as a Web Worker by app.js (solveChallenge).
// Finds n (0 <= n <= max) with sha256(salt + n) === challenge, the same computation the server verifies
// (ProofOfWorkBotProtection). Plain JavaScript SHA-256 (no dependencies); a 50,000-hash search takes ~0.1 s.
var K = new Uint32Array([
  0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
  0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
  0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
  0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
  0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
  0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
  0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
  0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2]);
var W = new Uint32Array(64);
var encoder = new TextEncoder();
function ror(x, n) { return (x >>> n) | (x << (32 - n)); }
function sha256hex(text) {
  var bytes = encoder.encode(text), len = bytes.length;
  var total = ((len + 9 + 63) >> 6) << 6, buf = new Uint8Array(total);
  buf.set(bytes); buf[len] = 0x80;
  var view = new DataView(buf.buffer);
  view.setUint32(total - 8, Math.floor(len / 0x20000000)); view.setUint32(total - 4, (len << 3) >>> 0);
  var h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a, h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19;
  for (var o = 0; o < total; o += 64) {
    for (var i = 0; i < 16; i++) W[i] = view.getUint32(o + i * 4);
    for (i = 16; i < 64; i++) {
      var s0 = ror(W[i - 15], 7) ^ ror(W[i - 15], 18) ^ (W[i - 15] >>> 3);
      var s1 = ror(W[i - 2], 17) ^ ror(W[i - 2], 19) ^ (W[i - 2] >>> 10);
      W[i] = (W[i - 16] + s0 + W[i - 7] + s1) >>> 0;
    }
    var a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, h = h7;
    for (i = 0; i < 64; i++) {
      var t1 = (h + (ror(e, 6) ^ ror(e, 11) ^ ror(e, 25)) + ((e & f) ^ (~e & g)) + K[i] + W[i]) >>> 0;
      var t2 = ((ror(a, 2) ^ ror(a, 13) ^ ror(a, 22)) + ((a & b) ^ (a & c) ^ (b & c))) >>> 0;
      h = g; g = f; f = e; e = (d + t1) >>> 0; d = c; c = b; b = a; a = (t1 + t2) >>> 0;
    }
    h0 = (h0 + a) >>> 0; h1 = (h1 + b) >>> 0; h2 = (h2 + c) >>> 0; h3 = (h3 + d) >>> 0;
    h4 = (h4 + e) >>> 0; h5 = (h5 + f) >>> 0; h6 = (h6 + g) >>> 0; h7 = (h7 + h) >>> 0;
  }
  var out = "", hs = [h0, h1, h2, h3, h4, h5, h6, h7];
  for (i = 0; i < 8; i++) out += ("00000000" + hs[i].toString(16)).slice(-8);
  return out;
}
function solve(salt, challenge, max) {
  for (var n = 0; n <= max; n++) if (sha256hex(salt + n) === challenge) return n;
  return null;
}
if (typeof self !== "undefined" && typeof self.postMessage === "function" && typeof module === "undefined") {
  self.onmessage = function (e) { self.postMessage(solve(e.data.salt, e.data.challenge, e.data.max)); };
}
if (typeof module !== "undefined") module.exports = { sha256hex: sha256hex, solve: solve };

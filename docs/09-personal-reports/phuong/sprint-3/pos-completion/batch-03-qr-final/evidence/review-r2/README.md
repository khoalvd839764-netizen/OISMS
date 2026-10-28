# B03 review R2 evidence

Reviewed 2026-10-28 Asia/Saigon on `fcf8fb2a4d35954ff5c7909930981b21cdd0fc9e`.

- `reviewer-probes.cjs`: actual QR service and PaymentModal transpiled into Node VM; React hook/JSX, API and runner I/O doubles. No browser or DB mutation. Run from repo root.
- `probe-results.json`: observed counterexamples, HEAD and source hashes. Exit0 means the defects were reproduced, not that acceptance passed.
- `probes.log`: console output of those probes.
- `b02-tests.log`: B02 suite from repo root, 24/24 PASS; network warnings are not live verification evidence.

Additional checks observed in reviewer tool output: baseline `npm test` 34/34 PASS, QR module 10/10 PASS, `npm run build` PASS (Vite 1.02s). No extra rerun solely to manufacture logs. Initial B02 invocation from app cwd failed due to harness expecting repo root; corrected root invocation is the saved log. Full details and limitations in `../../reviews/v1.2-integration-review-r2.md`.

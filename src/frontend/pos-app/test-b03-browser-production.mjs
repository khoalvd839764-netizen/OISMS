// Compatibility entry point. Replaces the v1.2 runner with mocked business APIs.
// Historical v1.2 screenshots prove rendering only, not full-stack acceptance.
import { runB03Live } from './test-b03-live.mjs';
await runB03Live();

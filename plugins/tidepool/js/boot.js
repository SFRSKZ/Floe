import { startHost } from './app.js';

// The entry point lives in its own file: Floe's plugin pages run under a strict CSP, and an inline
// module would be blocked by it.
startHost();

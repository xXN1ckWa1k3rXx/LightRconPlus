# Lua API v1

The full English reference is in [GitBook documentation](gitbook/README.md). Start with [Getting started](gitbook/getting-started/README.md), then see [Events](gitbook/events.md) and [Variables](gitbook/variables/README.md).

The [API manifest](../api/lightrcon-api.json) and [example plugins](../examples/plugins) are included in local release packages.

Lua enables `pcall`/`xpcall`. Uncaught errors print a script filename, source location and `stack traceback` in Plugin console, with absolute filesystem paths redacted. A failing timer is removed while other timers continue. The instruction budget terminates runaway callbacks even inside `pcall`; later events can still run. See [runtime behavior](gitbook/guides/runtime.md).

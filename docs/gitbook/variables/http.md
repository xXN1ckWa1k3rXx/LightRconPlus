# http

Synchronous HTTP(S) requests. Calls block the plugin queue. Request/header handling and body reading each have a 10-second timeout; a response body is limited to 1 MiB. Failures raise errors rather than returning a synthetic response.

## request

```lua
http.request(method, url[, body, headers])
```

Send an HTTP request. Response bytes are decoded as UTF-8. Non-2xx HTTP statuses still return a result table.

| Name | Type | Description |
| --- | --- | --- |
| method | string | HTTP method, such as GET or POST. |
| url | string | Absolute HTTP or HTTPS URL. |
| body | string / nil | Optional request body. |
| headers | table / nil | Optional header names mapped to scalar values. Content headers require a body. |

**Returns:** `table`: status (number), body (string), success (boolean for a 2xx status).

```lua
local ok, response = pcall(http.request, "GET", "https://example.com")
if ok then
    log.info(tostring(response.status))
else
    log.warning(tostring(response))
end
```

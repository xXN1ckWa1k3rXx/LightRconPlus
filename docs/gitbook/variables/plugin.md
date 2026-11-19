# plugin

Metadata table used by Details. It contains fields, not functions. Assign it while the script loads. An omitted api_version is accepted; explicitly declaring 1 is recommended. An unsupported declared version prevents loading.
| Name | Type | Description |
| --- | --- | --- |
| api_version | number | Supported value: 1. |
| name | string | Display name; defaults to the file name without extension. |
| version | string | Plugin version. |
| author | string | Author name. |
| website | string | Project URL. |
| description | string | Plugin description; displayed as text. |

```lua
plugin = { api_version = 1, name = "Example", version = "1.0" }
```

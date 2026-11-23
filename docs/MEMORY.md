# Memory use in Pre-alpha 1.2

Both executables use workstation GC, concurrent collection and `System.GC.ConserveMemory=9`. This asks .NET to retain less free heap space and compact the large object heap more readily. It can increase collection frequency and CPU/latency under load; it is not a hard memory limit. The application does not periodically force GC or trim the Windows working set.

Additional changes reduce recurring allocation:

- Profile saves capture each profile directly into a JSON element under its lock instead of serializing to a string and deserializing a duplicate object graph before writing.
- Layer frames serialize into one pooled UTF-8 buffer with a length prefix. The pool storage is cleared on return, including cancellation paths, because authentication frames contain passwords.
- Native C# plugins no longer run an empty 100 ms timer and enqueue no-op tick work. Lua retains its serialized timer behavior.
- Legacy plugin classification skips a Roslyn syntax tree when source contains no native assembly marker.

## Local comparison

On Windows x64 with .NET 10, two fresh-process runs compared the previous 1.1 self-contained Host against the 1.2 self-contained Host. Each used the same loopback Frostbite simulator, one enabled Lua plugin and one enabled PRoCon plugin, then 1,000 chat events with 100 extra characters per message. After startup/plugin compilation and the burst, three samples were taken at five-second intervals following a 15-second settling period. Tests ran locally, not on the production server.

| Host process metric | Previous 1.1 | Pre-alpha 1.2 |
| --- | --- | --- |
| Run 1 private memory | 92.30–95.11 MiB | 39.28–42.27 MiB |
| Run 2 private memory | 79.45–81.01 MiB | 41.02–42.72 MiB |
| Run 1 working set | 129.09–131.51 MiB | 112.03–114.43 MiB |
| Run 2 working set | 122.88–124.05 MiB | 113.25–114.74 MiB |

The median private-memory reductions were about 57% and 48%. These samples cover the Host process only. The separate PRoCon Worker, startup compilation peak, loaded plugin behavior, player/log volume and long-running growth all affect actual deployment use. This benchmark does not establish a memory-leak fix or guarantee those numbers on other servers.

Removing Pictures, API, examples and developer documentation from the ZIP reduces distribution size. It does not reduce process memory by itself; those files were not loaded into RAM merely because they were packaged.

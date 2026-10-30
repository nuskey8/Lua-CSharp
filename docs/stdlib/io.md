# Files and standard I/O

[Reference index](README.md) · [Official Lua 5.2 I/O library](https://www.lua.org/manual/5.2/manual.html#6.8) · [Library implementation](../../src/Lua/Standard/IOLibrary.cs) · [File methods](../../src/Lua/Standard/FileHandle.cs)

Install with `state.OpenIOLibrary()`. Operations use `LuaPlatform.FileSystem` and the standard-stream handles captured from `LuaPlatform.StandardIO` when the library opens. A custom provider can use memory, assets, or another storage system.

## Library APIs

| API / official reference | Inputs and result | Lua-CSharp differences and reasons |
| --- | --- | --- |
| [`io.close([file])`](https://www.lua.org/manual/5.2/manual.html#pdf-io.close) | Closes the supplied file, or the default output when no argument is supplied. Explicit nil is an error. | Default console standard handles reject closing. File failures use managed messages/error codes; see below. |
| [`io.flush()`](https://www.lua.org/manual/5.2/manual.html#pdf-io.flush) | Flushes the current default output. | Calls the configured stream's flush operation; it is not necessarily an OS file stream. |
| [`io.input([file])`](https://www.lua.org/manual/5.2/manual.html#pdf-io.input) | Gets the default input, selects an open handle, or opens a filename for reading and selects it. | **Host-dependent:** filenames go through the filesystem provider. Omitted/nil input is a getter; invalid/closed handles are rejected before changing the default. |
| [`io.lines([filename, ...formats])`](https://www.lua.org/manual/5.2/manual.html#pdf-io.lines) | Iterates using read formats. A filename creates an automatically closed file; omitted/nil filename uses default input without automatic close. Default format is `*l`. | **Limitation:** at EOF the iterator returns a nil result instead of zero results. Generic-for termination is unchanged, but `select('#', iterator())` can differ. This shares the managed read helper's result convention. Closed default input is rejected when creating the iterator. |
| [`io.open(filename [, mode])`](https://www.lua.org/manual/5.2/manual.html#pdf-io.open) | Opens a file, defaulting to `r`; returns a handle or `nil, message, code` for handled open failures. | String mode, rather than `b`, determines the built-in stream's byte/text encoding. Errors include .NET messages/HResult codes. Custom providers can supply different storage semantics. |
| [`io.output([file])`](https://www.lua.org/manual/5.2/manual.html#pdf-io.output) | Gets/selects default output, or opens a filename for writing and selects it. | Switching output flushes the previous open output in this implementation. This is an additional effect of its explicit managed flush policy. Invalid/closed arguments are validated before that flush. It does not redirect `print`, which uses `StandardIO.Output` directly. |
| [`io.popen(command [, mode])`](https://www.lua.org/manual/5.2/manual.html#pdf-io.popen) | Officially opens a process pipe. | **Unavailable:** not registered; the built-in I/O abstraction does not implement process pipes. A host can expose its own process/stream binding. |
| [`io.read(...)`](https://www.lua.org/manual/5.2/manual.html#pdf-io.read) | Reads from the selected default input using the formats below. | Shares `file:read` semantics, including string units and provider error behavior. |
| [`io.stderr`](https://www.lua.org/manual/5.2/manual.html#pdf-io.stderr) | Initial standard error handle. | **Host-dependent:** wraps the configured error stream; default console handles cannot be closed through Lua. |
| [`io.stdin`](https://www.lua.org/manual/5.2/manual.html#pdf-io.stdin) | Initial standard input handle. | **Host-dependent:** wraps the configured input stream. Selecting another `io.input` does not replace this field. |
| [`io.stdout`](https://www.lua.org/manual/5.2/manual.html#pdf-io.stdout) | Initial standard output handle. | **Host-dependent:** wraps the configured output stream. Selecting another `io.output` does not replace this field. |
| [`io.tmpfile()`](https://www.lua.org/manual/5.2/manual.html#pdf-io.tmpfile) | Creates a temporary read/write file handle. | The default provider uses a .NET `FileStream` with `DeleteOnClose`. **Limitation:** creation failures are raised rather than converted to Lua's usual nil/error result; this entry directly awaits the provider. Close explicitly for deterministic deletion. |
| [`io.type(value)`](https://www.lua.org/manual/5.2/manual.html#pdf-io.type) | Returns `file`, `closed file`, or nil. | Recognizes managed `FileHandle` objects and queries their provider's open state. |
| [`io.write(...)`](https://www.lua.org/manual/5.2/manual.html#pdf-io.write) | Writes string/number arguments to default output and returns that file on success. | Numbers use `NumberFormat`; strings use the configured stream encoding. |

## File methods

| API / official reference | Inputs and result | Lua-CSharp differences and reasons |
| --- | --- | --- |
| [`file:close()`](https://www.lua.org/manual/5.2/manual.html#pdf-file:close) | Closes the underlying stream; reports success or handled I/O failure. | Calls `ILuaStream.CloseAsync`; close behavior belongs to the provider. The default stream flushes and disposes the underlying .NET stream. Lua GC finalizers are not used to implement deterministic close. |
| [`file:flush()`](https://www.lua.org/manual/5.2/manual.html#pdf-file:flush) | Flushes pending writes. | Calls the provider's flush operation. |
| [`file:lines(...)`](https://www.lua.org/manual/5.2/manual.html#pdf-file:lines) | Returns an iterator over the file using the given formats; does not automatically close the handle. | Closed handles fail at iterator creation. **Limitation:** the EOF result count follows the same nil-versus-no-results difference as `io.lines`. |
| [`file:read(...)`](https://www.lua.org/manual/5.2/manual.html#pdf-file:read) | Returns one value per successful format, stopping at the first failed conversion/EOF with nil. No formats means `*l`. | Default streams decode UTF-8 to UTF-16, while byte-mode streams preserve raw bytes. Counts use the selected representation. Numeric scanning remains byte-based, even in UTF-16 mode. |
| [`file:seek([whence [, offset]])`](https://www.lua.org/manual/5.2/manual.html#pdf-file:seek) | Seeks using `set`, `cur`, or `end`; defaults are `cur, 0`. Returns the resulting position or handled I/O failure. | Offsets/positions are **bytes** in built-in streams, including UTF-16 mode. Buffered unread bytes are accounted for. If a counted read returned only the high surrogate of a supplementary character, its bytes have already been consumed; a position query retains the pending low surrogate. Underlying seek support is provider-dependent. |
| [`file:setvbuf(mode [, size])`](https://www.lua.org/manual/5.2/manual.html#pdf-file:setvbuf) | Selects `no`, `line`, or `full` buffering policy; returns true when accepted. | **Limitation:** built-in `LuaStream` changes flush policy, not the underlying .NET buffer allocation. `no` flushes each write; `line` flushes writes containing LF; `full` uses a per-write size threshold rather than a cumulative C stdio buffer. `size` does not resize the stream's buffer. Custom providers may implement their own policy. |
| [`file:write(...)`](https://www.lua.org/manual/5.2/manual.html#pdf-file:write) | Writes strings/numbers and returns the file on success. | Default streams encode UTF-16 text as UTF-8; byte-mode streams write raw byte values. Numbers use `NumberFormat`. |

## Formats, modes, and errors

The supported read formats are `*n`, `*a`, `*l`, `*L`, and a numeric count. The format dispatcher also accepts `*number`, `*all`, and `*line`. Bare `L` is an additional accepted alias for `*L`; use `*L` for the Lua 5.2 spelling. `*a` returns an empty string at EOF; a count of zero probes EOF without consuming a character. Numeric counts are truncated and must fit a nonnegative managed `int`.

With built-in streams:

| Operation | Default UTF-16 mode | Byte mode |
| --- | --- | --- |
| `read(n)` | Up to `n` UTF-16 units; supplementary characters can be split across results | Up to `n` raw bytes |
| `read('*l')` | CR, LF, and CRLF end a line; the ending is removed | LF ends a line; a preceding CR remains in the result |
| `read('*L')` | Keeps the original line ending | Keeps LF and any preceding CR |
| Initial UTF-8 BOM in a text read | Skipped only at the beginning | Preserved as data |
| Invalid UTF-8 | Replaced when decoded | Preserved as bytes |

`*n` scans decimal/hex numeric bytes and consumes incomplete numeric input items when a conversion fails. It does not decode Unicode whitespace or skip a UTF-8 BOM as a Unicode text read would. This is the numeric scanner's implementation boundary.

Recognized open modes are `r`, `rb`, `r+`, `r+b`, `w`, `wb`, `w+`, `w+b`, `a`, `ab`, `a+`, and `a+b`. The `b` marker does not change string mode: built-in `LuaStream` encoding was selected by its constructor/platform. Use a matching byte-mode platform for binary files, including `string.dump` output.

Handled failures generally return `nil, message, code`; closed-file misuse raises an error. Managed error codes are exception `HResult` values, not C `errno` values. Some provider exceptions propagate as Lua errors instead of becoming a result tuple. See [shared cancellation behavior](README.md#async-calls-cancellation-and-errors).

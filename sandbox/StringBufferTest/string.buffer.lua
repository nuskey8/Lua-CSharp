--- @meta

--- @class string.buffer.options
--- @field maxRecursions number?
--- @field dict string[]?
--- @field metatable metatable[]

--- @class string.buffer
local stringBuffer = {}

--- @param size integer
--- @param options string.buffer.options
function stringBuffer.new(size, options) end

--- @param value any
--- @return string data
--- @nodiscard
function stringBuffer.encode(value) end

--- @param data string
--- @return string
--- @nodiscard
function stringBuffer.decode(data) end

--- @class string.buffer.object : userdata
local stringBufferObject = {}

--- @param value any
--- @return self
function stringBufferObject:encode(value) end

--- @return any
function stringBufferObject:decode() end

--- @return self
function stringBufferObject:reset() end

--- @return self
function stringBufferObject:free() end

--- @param ... string | number | table
--- @return self
function stringBufferObject:put(...) end

--- @param fmt string
--- @param ... any
function stringBufferObject:putf(fmt, ...) end

--- @param str string
function stringBufferObject:set(str) end

--- @param len integer
--- @return self
function stringBufferObject:skip(len) end

--- @param ... integer
--- @return string ...
function stringBufferObject:get(...) end

--- @return string str
--- @nodiscard
function stringBufferObject:tostring() end

--- @return integer len
function stringBufferObject:length() end

return stringBuffer

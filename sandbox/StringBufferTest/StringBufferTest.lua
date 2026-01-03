local stringBuffer = require("string.buffer")

local bufferObject = stringBuffer.new()

local encoded1 = stringBuffer.encode({
	1, 2, 3, 4, 5,
	[1.5] = -15,
	[2^32] = "2^32",
})

print("[encoded1]")
print(encoded1)

local decoded1 = stringBuffer.decode(encoded1)
print("[decoded1]")
print(decoded1)
for key, value in pairs(decoded1) do
	print("", key, ": ", value)
end

print("\n\n")

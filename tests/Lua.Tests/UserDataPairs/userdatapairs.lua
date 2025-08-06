local iterations = 0

print("LuaList pairs:")
for k, v in pairs(LuaList) do
    print("List[" .. tostring(k) .. "] = " .. tostring(v))
    assert(k == v)
    iterations = iterations + 1
end
assert(iterations == 5)


iterations = 0
print("LuaList ipairs:")
for i, v in ipairs(LuaList) do
    print("List[" .. tostring(i) .. "] = " .. tostring(v))
    assert(i == v)
    iterations = iterations + 1
end
assert(iterations == 5)


iterations = 0
print("LuaList next:")
local i, v = next(LuaList, nil)
while i do
    print("List[" .. tostring(i) .. "] = " .. tostring(v))
    assert(i == v)
    iterations = iterations + 1

    i, v = next(LuaList, i)
end
assert(iterations == 5)


local t =
{
    1, 2, 3, 4, 5,
    ["some key"] = "some value",
    ["another key"] = "another value",
    [-1000] = -1001,
    [_G] = print,
}

print("LuaTable pairs:")
for k, v in pairs(t) do
    print(k, v)
end
print("LuaTable ipairs:")
for i, v in ipairs(t) do
    print(i, v)
end

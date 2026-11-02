local function add(a, b)
    local result = a + b
    return result
end

local total = 0
for i = 1, 3 do
    total = add(total, i)
end
print("total", total)

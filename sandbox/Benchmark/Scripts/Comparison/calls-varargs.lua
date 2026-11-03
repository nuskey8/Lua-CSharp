local function add(a, ...)
    local b, c = ...
    return a + b + c
end
local sum = 0
for i = 1, 2000 do
    sum = sum + add(i, 2, 3)
end
return sum

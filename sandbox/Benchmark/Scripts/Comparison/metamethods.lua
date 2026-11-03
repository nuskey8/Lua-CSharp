local mt = {
    __add = function(a, b) return a.x + b.x end,
    __index = function(t, k) return 5 end,
}
local a, b = setmetatable({ x = 3 }, mt), setmetatable({ x = 4 }, mt)
local sum = 0
for i = 1, 2000 do
    sum = sum + (a + b) + a.missing
end
return sum

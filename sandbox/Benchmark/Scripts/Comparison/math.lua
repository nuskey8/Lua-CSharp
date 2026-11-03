local abs, sqrt, min, max = math.abs, math.sqrt, math.min, math.max
local sum = 0
for i = 1, 2000 do
    sum = sum + min(abs(-i), sqrt(i)) + max(i, 3, 7)
end
return sum

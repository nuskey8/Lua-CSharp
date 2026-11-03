local values = {}
for i = 1, 1000 do
    values[i] = { x = i, y = i % 7 }
end
local sum = 0
for i = 1, 1000 do
    local value = values[i]
    sum = sum + value.x + value.y
end
return sum

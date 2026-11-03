local add = host_add
local sum = 0
for i = 1, 2000 do
    sum = add(sum, i)
end
return sum

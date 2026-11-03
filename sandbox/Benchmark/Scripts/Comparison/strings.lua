local rep, reverse, sub, byte, concat = string.rep, string.reverse, string.sub, string.byte, table.concat
local count = 0
local values = { 'abc', 12, 'def', 34 }
for i = 1, 400 do
    local text = sub(reverse(rep('abcd', 8, ':')), 2, -2)
    count = count + byte(text) + #text + #concat(values, ',')
end
return count

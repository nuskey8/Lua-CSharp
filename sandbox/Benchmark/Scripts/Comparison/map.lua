-- String-keyed hash table: insert, read/update, delete, miss, then traverse.
local map, keys = {}, {}
for i = 1, 1000 do
    local key = 'key-' .. i
    keys[i] = key
    map[key] = i * 3
end

local sum = 0
for round = 1, 4 do
    for i = 1, 1000 do
        local key = keys[i]
        local value = map[key]
        sum = sum + value
        map[key] = value + round
    end
end

for i = 2, 1000, 2 do
    map[keys[i]] = nil
end
for i = 2, 1000, 2 do
    assert(map[keys[i]] == nil)
end
for key, value in pairs(map) do
    sum = sum + value
end
-- Read/update sum: 12 * sum(1..1000) + 10 * 1000.
-- Remaining odd keys: 3 * sum(1,3,..999) + 10 * 500.
return sum

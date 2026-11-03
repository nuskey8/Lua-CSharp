local co = coroutine.create(function()
    for i = 1, 500 do
        coroutine.yield(i)
    end
end)
local sum = 0
for i = 1, 500 do
    local ok, value = coroutine.resume(co)
    assert(ok)
    sum = sum + value
end
return sum

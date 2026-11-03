local gsub = string.gsub
local count = 0
for i = 1, 400 do
    local text = gsub('hello 123 world 456', '(%a+)', '[%1]')
    count = count + #text
end
return count

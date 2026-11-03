local movement = require("movement")
local greeting = host.read_text("greeting.txt")
local actor = { name = "player", position = 0, speed = 1.5 }
local updates = 0

host.log(greeting)

return function(frame, dt)
    updates = updates + 1
    local position = movement.advance(actor, dt)
    if updates % 4 == 0 then
        host.log(string.format("frame %d: %s at %.2f", frame, actor.name, position))
    end
    return position
end

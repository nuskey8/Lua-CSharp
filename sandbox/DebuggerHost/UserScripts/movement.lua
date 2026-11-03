local movement = {}

local function distance(speed, dt)
    return speed * dt
end

function movement.advance(actor, dt)
    local step = distance(actor.speed, dt)
    actor.position = actor.position + step
    return actor.position
end

return movement

-- Sum escape iteration counts over a 128 x 96 grid, capped at 64.
-- No image output or I/O is included in the workload.
local width, height, limit = 128, 96, 64
local total = 0
for y = 0, height - 1 do
    local ci = y / height * 2.0 - 1.0
    for x = 0, width - 1 do
        local cr = x / width * 3.0 - 2.0
        local zr, zi = 0.0, 0.0
        local iterations = 0
        while iterations < limit and zr * zr + zi * zi <= 4.0 do
            local next_r = zr * zr - zi * zi + cr
            zi = 2.0 * zr * zi + ci
            zr = next_r
            iterations = iterations + 1
        end
        total = total + iterations
    end
end
return total

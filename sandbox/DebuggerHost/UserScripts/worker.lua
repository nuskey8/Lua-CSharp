local pricing = require("pricing")

return function(job)
    local total = pricing.total(job.unit_price, job.quantity)
    local message = host.read_text("greeting.txt")
    host.log(string.format("job %d: %s", job.id, message))
    return total
end

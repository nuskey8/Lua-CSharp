local pricing = {}

function pricing.total(unit_price, quantity)
    local subtotal = unit_price * quantity
    local tax = subtotal * 0.1
    return subtotal + tax
end

return pricing

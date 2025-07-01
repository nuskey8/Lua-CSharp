local t = {}

t.greet = function()
    print("Bar: Hello!")
    print(debug.traceback())
end

return t

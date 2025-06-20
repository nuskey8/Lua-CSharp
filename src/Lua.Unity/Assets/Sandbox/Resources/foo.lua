local t = {}

t.greet = function()
    print("Foo: Hello!")
end

print(debug.traceback())
return t

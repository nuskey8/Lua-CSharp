function f1()
	local c = {}
	return #c
end

function f2(a, b)
	assert(b == nil)
end

f1()
f2()

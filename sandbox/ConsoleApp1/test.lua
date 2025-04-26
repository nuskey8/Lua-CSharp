
function f(n, a, ...)
  local b
    n, b, a = n-1, ..., a
    assert(b == ...)
end

# String Buffer Library

这是一个为 **Lua-CSharp** 提供的 `string.buffer` 模块实现，
设计灵感与接口行为主要参考 [**LuaJIT 的 `string.buffer`**](https://luajit.org/ext_buffer.html) 模块，
用于高效的字符串拼接与 Lua 值的二进制序列化 / 反序列化。

该模块在 Lua 层提供与 LuaJIT 类似的 API，
在 C# 层则以可扩展的 `StringBuffer` 抽象实现，支持自定义序列化 **IUserData**, **LightUserData**。

## 它能做什么

- 构建长字符串
- 序列化/反序列化游戏数据
- 快速克隆表

### 快速克隆表

你可以轻易地实现一个高性能表克隆函数，前提是表内没有任何不可序列化的数据。

```lua
local stringBuffer = require("string.buffer")
local encode = stringBuffer.encode
local decode = stringBuffer.decode

function fastCopy(tbl)
  return decode(encode(tbl))
end
```

以下是在我的桌面系统中的测试结果，在 `DEBUG` 构建模式下，它比在纯lua里实现的克隆函数快`4`倍。
若引入 `options` 参数，并指定常用字符串的映射，速度会更快。

```
=============== copy speed test ==================
deepCopy:	1.640625
fastCopy:	0.453125
```

详见测试案例：[StringBufferTest](../sandbox/StringBufferTest/StringBufferTest.lua)

## 如何使用

在创建了 **LuaState** 之后，调用`LuaState.OpenStringBufferLibrary()`。

```cs
LuaState state = LuaState.Create();
state.OpenStringBufferLibrary();
state.OpenModuleLibrary();
```

打开了 StringBufferLibrary 后并不会将模块暴露到全局环境中，你在lua侧需要手动引入该模块 `require("string.buffer")`。

## API 预览

该库提供了 vscode 插件支持 [Lua Language Server](https://luals.github.io/)，详见 [string.buffer.lua](../sandbox/StringBufferTest/string.buffer.lua)。

### **表定义**

#### string.buffer.options

字符串缓冲对象的编码选项

##### 字段

- **maxRecursions**: number | nil
表最大递归深度：对一个表进行序列化时，若深度超过了该值，则要么抛出异常，要么忽略子表，异常行为见 `suppressErrors`。默认值 `32`，内部值类型为 `System.Int32`。

- **suppressErrors**: boolean | nil
抑制错误：当设置为 `true` 时，表序列化将不会抛出 *递归* 异常。默认值 `true`，内部值类型为 `System.Boolean`。

- **dict**: string[] | nil
键名表：包含一个字符串数组，这些字符串应该作为您正在序列化的对象的表中出现。在序列化过程中，这些键/值会被紧凑地编码为索引，因此选择合适的值可以节省空间并提高序列化性能。

- **metatable**: table[] | nil
元表：包含一个元表数组，这些元表会记录序列化的表对象的元表。

### **string.buffer**

#### string.buffer.new(size, options)

创建一个 `string.buffer.object` 对象。

##### 参数

- **size**: `number` | `nil`
初始缓冲区大小，在内部会被转换成值类型 `System.Int32` 类型，默认值 `32`。

- **options**: `string.buffer.options` | `nil`

##### 返回值

- `string.buffer.object`

#### string.buffer.encode(value)

将一个 Lua 值序列化为字符串。

支持序列化的类型包括：

- `nil`
- `boolean`
- `number`
- `string`
- `table`

以下类型 **默认不支持**，调用时会抛出异常，除非在 C# 层进行了扩展：

- `userdata`
- `lightuserdata`

以下类型 **完全不支持**：

- `function`
- `thread`

##### 参数

- **value**: `any`
待序列化的 Lua 值。

##### 返回值

- `string`
序列化后的二进制字符串（字节数组）。

#### string.buffer.decode(str)

将字符串反序列化为 Lua 值。

##### 参数

- **str**: `string`
由 `string.buffer.encode` 生成的字符串。

##### 返回值

- `any`
反序列化得到的 Lua 值。

### string.buffer.object

#### string.buffer.object:encode(value)

将值的字节数据拼接到缓冲对象内部。

见 ***string.buffer.encode(value)***

##### 参数

- **value**: `any`
待序列化的 Lua 值。

##### 返回值

- `string.buffer.object`
原字符串缓冲对象，用于链式调用。

#### string.buffer.object:decode(value)

见 ***string.buffer.decode(value)***

##### 参数

- **value**: `string`
由 `string.buffer.decode` 生成的字符串。

##### 返回值

- `any`
反序列化得到的 Lua 值。

#### string.buffer.object:reset()

清空缓冲区，但已分配的空间不会被释放，可以被复用。

##### 返回值

- `string.buffer.object`
原字符串缓冲对象，用于链式调用。

#### string.buffer.object:free()

强制释放内存，所有分配的空间将会被清理，等待垃圾回收，但不会销毁实例，因此该对象之后仍然可以继续使用。

#### string.buffer.object:put(...)

将特定字节数据追加到字符串缓冲对象里。

##### 参数

- **...**: `string` | `number` | `table`
数据参数，所有数据在内部都会变成字节数据。注传入的表必须含带有 `__tostring` 字段的元表，否则会抛出异常。

##### 返回值

- `string.buffer.object`
原字符串缓冲对象，用于链式调用。

#### string.buffer.object:putf(fmt, ...)

将特定字节数据格式化地追加到字符串缓冲对象里，格式遵循 `string.format`。

##### 参数

-- **fmt**: `string`
格式化字符串。

-- **...**
格式化参数。

##### 返回值

- `string.buffer.object`
原字符串缓冲对象，用于链式调用。

#### string.buffer.object:set(str)

将字符串数据拷贝到字符串缓冲对象中。
与 `string.buffer.object:put` 不同，它不会追加字节数据，在调用该方法后，原来的数据都会被替换成新的数据。

##### 参数

- **str**: `string`
待写入的字符串。

##### 返回值

- `string.buffer.object`
原字符串缓冲对象，用于链式调用。

#### string.buffer.object:skip(len)

移除指定长度的字节数据。

##### 参数

- **len**: `number`
要移除的字节长度。内部会转化为 `System.Int32` 类型。

##### 返回值

- `string.buffer.object`
原字符串缓冲对象，用于链式调用。

#### string.buffer.object:get(...)

移除指定长度的字节数据，并将所有移除的字节数据转化为 `string` 返回。

##### 参数

- **...**: `integer`
待读取的字节数组长度。
若参数数量为 `0` ，则应用到整个或剩下的字节数据

##### 返回值

- ... `string`
数个从字节数据中转换而来的字符串。

#### string.buffer.object:tostring()

将当前字节数据转化成 `string` 并返回，该操作不会移除字节数据。

返回的字符串值在缓冲对象会被内部缓存，若后续不对字节数据操作，则多次调用该函数不会分配新的字符串内存。

##### 返回值

- `string`
字符串（带缓存）。

#### string.buffer.object:length()

获取字节数据的长度。

由于目前 `Lua-CSharp` 带一些特性，导致无法通过 #string.buffer.object 获取长度（总是返回nil），因此追加了该函数。

##### 返回值

- `number`
字节数据长度。

## 其他事项

- 目前无法通过 `#` 来获取字节数据长度，可能是 `Lua-CSharp` 的一个bug？

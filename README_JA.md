# Lua-CSharp

High performance Lua interpreter implemented in C# for .NET and Unity

![img](docs/Header.png)

[![NuGet](https://img.shields.io/nuget/v/LuaCSharp.svg)](https://www.nuget.org/packages/LuaCSharp)
[![Releases](https://img.shields.io/github/release/nuskey8/Lua-CSharp.svg)](https://github.com/nuskey8/Lua-CSharp/releases)
[![license](https://img.shields.io/badge/LICENSE-MIT-green.svg)](LICENSE)

[English]((./README.md)) | 日本語

## 概要

Lua-CSharpはC#実装のLuaインタプリタを提供するライブラリです。Lua-CSharpを導入することで、.NETアプリケーション内で簡単にLuaスクリプトを組み込むことが可能になります。

Lua-CSharpは最新のC#の機能を活用し、低アロケーション・ハイパフォーマンスを念頭において設計されています。Lua-CSharpはC#アプリケーションに組み込まれることを前提としているため、C#-Lua間の相互運用時に最大のパフォーマンスを発揮するように最適化されています。以下は[MoonSharp](https://github.com/moonsharp-devs/moonsharp/), [NLua](https://github.com/NLua/NLua)と比較したベンチマークです。

![img](docs/Benchmark.png)

MoonSharpは多くの場合で十分な速度を発揮しますが、設計上非常に大きいアロケーションが発生します。NLuaはCバインディングであるため動作そのものは高速ですが、C#レイヤーとのやり取りの際に大きなオーバーヘッドがかかります。Lua-CSharpは完全にC#で実装されているため、C#コードとオーバーヘッドなしでやり取りが可能です。また、IL生成などを一切使用しないためAOT環境でも安定して動作します。

## 特徴

* C#で実装されたLua5.2インタプリタ
* async/awaitに統合された扱いやすいAPI
* try-catchによる例外処理のサポート
* 最新のC#を活用したハイパフォーマンスな実装
* Unityサポート(Mono/IL2CPPの両方で動作)

## インストール

### NuGet packages

Lua-CSharpを利用するには.NET Standard2.1以上が必要です。パッケージはNuGetから入手できます。

### .NET CLI

```ps1
dotnet add package LuaCSharp
```

### Package Manager

```ps1
Install-Package LuaCSharp
```

### Unity

Lua-CSharpをUnityで利用することも可能です。詳細は[Lua.Unity](#luaunity)の項目を参照してください。

## クイックスタート

`LuaState`クラスを利用することでC#上からLuaスクリプトを実行することが可能です。以下はLuaで記述された簡単な演算を評価するサンプルコードです。

```cs
using Lua;

// LuaStateを作成する
var state = LuaState.Create();

// DoStringAsyncで文字列のLuaスクリプトを実行する
var results = await state.DoStringAsync("return 1 + 1");

// 2
Console.WriteLine(results[0]);
```

> [!WARNING]
> `LuaState`はスレッドセーフではありません。同時に複数のスレッドからアクセスしないでください。

## LuaValue

Luaスクリプト上の値は`LuaValue`型で表現されます。`LuaValue`の値は`TryRead<T>(out T value)`または`Read<T>()`で読み取ることが可能です。

```cs
var results = await state.DoStringAsync("return 1 + 1");

// double
var value = results[0].Read<double>();
```

また、`Type`プロパティから値の型を取得できます。

```cs
var isNil = results[0].Type == LuaValueType.Nil;
```

Lua-C#間の型の対応を以下に示します。

| Lua        | C#               |
| ---------- | ---------------- |
| `nil`      | `LuaValue.Nil`   |
| `boolean`  | `bool`           |
| `string`   | `string`         |
| `number`   | `double`,`float` |
| `table`    | `LuaTable`       |
| `function` | `LuaFunction`    |
| `userdata` | `LuaUserData`    |
| `thread`   | `LuaThread`      |

C#側から`LuaValue`を作成する際には、変換可能な型の場合であれば暗黙的に`LuaValue`に変換されます。

```cs
LuaValue value;
value = 1.2;           // double   ->  LuaValue
value = "foo";         // string   ->  LuaValue
value = new LuaTable() // LuaTable ->  LuaValue
```

## LuaTable

Luaのテーブルは`LuaTable`型で表現されます。これは通常の`LuaValue[]`や`Dictionary<LuaValue, LuaValue>`のように使用できます。

```cs
// Lua側でテーブルを作成
var results = await state.DoStringAsync("return { a = 1, b = 2, c = 3 }")
var table1 = results[0].Read<LuaTable>();

// 1
Console.WriteLine(table1["a"]);

// テーブルを作成
results = await state.DoStringAsync("return { 1, 2, 3 }")
var table2 = results[0].Read<LuaTable>();

// 1 (Luaの配列は1-originであることに注意)
Console.WriteLine(table2[1]);
```

## グローバル環境

`state.Environment`からLuaのグローバル環境にアクセスできます。このテーブルを介して簡単にLua-C#間で値をやり取りすることが可能です。

```cs
// a = 10を設定
state.Environment["a"] = 10;

var results = await state.DoStringAsync("return a");

// 10
Console.WriteLine(results[0]);
```

## 標準ライブラリ

Luaの標準ライブラリを利用することも可能です。`state.OpenStandardLibraries()`を呼び出すことで、`LuaState`に標準ライブラリのテーブルを追加します。

```cs
using Lua;
using Lua.Standard;

var state = LuaState.Create();

// 標準ライブラリを追加
state.OpenStandardLibraries();

var results = await state.DoStringAsync("return math.pi");
Console.WriteLine(results[0]); // 3.141592653589793
```

標準ライブラリについては[Lua公式のマニュアル](https://www.lua.org/manual/5.2/manual.html#6)を参照してください。

> [!WARNING]
> Lua-CSharpは標準ライブラリの全ての関数をサポートしているわけではありません。詳細は[互換性](#互換性)の項目を参照してください。

## 関数

Luaの関数は`LuaFunction`型で表現されます。`LuaFunction`によってLuaの関数をC#側から呼び出したり、C#で定義した関数をLua側から呼び出したりすることが可能です。

### Luaの関数をC#から呼び出す

```lua
-- lua2cs.lua

local function add(a, b)
    return a + b
end

return add;
```

```cs
var results = await state.DoFileAsync("lua2cs.lua");
var func = results[0].Read<LuaFunction>();

// 引数を与えて関数を実行する
var funcResults = await func.InvokeAsync(state, [1, 2]);

// 3
Console.WriteLine(funcResults[0]);
```

### C#の関数をLua側から呼び出す

ラムダ式からLuaFunctionを作成することが可能です。

```cs
// グローバル環境に関数を追加
state.Environment["add"] = new LuaFunction((context, buffer, ct) =>
{
    // context.GetArgument<T>()で引数を取得
    var arg0 = context.GetArgument<double>(0);
    var arg1 = context.GetArgument<double>(1);

    // バッファに戻り値を記録
    buffer.Span[0] = arg0 + arg1;

    // 戻り値の数を返す
    return new(1);
});

// Luaスクリプトを実行
var results = await state.DoFileAsync("cs2lua.lua");

// 3
Console.WriteLine(results[i]);
```

```lua
-- cs2lua.lua

return add(1, 2)
```

> [!TIP]
> `LuaFunction`による関数の定義はやや記述量が多いため、関数をまとめて追加する際には`[LuaObject]`属性によるSource Generatorの使用を推奨します。詳細は[LuaObject](#luaobject)の項目を参照してください。

## async/awaitとの統合

`LuaFunction`は非同期メソッドとして動作します。そのため、以下のような関数を定義することでLua側から処理の待機を行うことも可能です。

```cs
// 与えられた秒数だけTask.Delayで待機する関数を定義
state.Environment["wait"] = new LuaFunction(async (context, buffer, ct) =>
{
    var sec = context.GetArgument<double>(0);
    await Task.Delay(TimeSpan.FromSeconds(sec));
    return 0;
});

await state.DoFileAsync("sample.lua");
```

```lua
-- sample.lua

print "hello!"

wait(1.0) -- 1秒待機する

print "how are you?"

wait(1.0) -- 1秒待機する

print "goodbye!"
```

このコードは以下の図のように、awaitで待機した後にLuaスクリプトの実行を再開することができます。これはゲームに組み込むスクリプトを記述する際などに非常に有用です。

![img](docs/img1.png)

## コルーチン

Luaのコルーチンは`LuaThread`型で表現されます。

コルーチンはLuaスクリプト内で利用できるだけでなく、Luaで作成したコルーチンをC#で待機することも可能です。

```lua
-- coroutine.lua

local co = coroutine.create(function()
    for i = 1, 10 do
        print(i)
        coroutine.yield()
    end
end)

return co
```

```cs
var results = await state.DoFileAsync("coroutine.lua");
var co = results[0].Read<LuaThread>();

for (int i = 0; i < 10; i++)
{
    var resumeResults = await co.ResumeAsync(state);

    // coroutine.resume()と同様、成功時は最初の要素にtrue、それ以降に関数の戻り値を返す
    // 1, 2, 3, 4, ...
    Console.WriteLine(resumeResults[1]);
}
```

## LuaObject

`[LuaObject]`属性を用いることで、Lua上で動作する独自のクラスを作成することが可能になります。Luaで使いたいクラスにこの属性を付加することで、Source GeneratorがLua側から利用されるコードを自動生成します。

以下のサンプルコードはLuaで動作する`System.Numerics.Vector3`のラッパークラスの実装例です。

```cs
using System.Numerics;
using Lua;

var state = LuaState.Create();

// 定義したLuaObjectのインスタンスをグローバル変数として追加する
// (LuaObjectを追加したクラスにはLuaValueへの暗黙的変換が自動で定義される)
state.Environment["Vector3"] = new LuaVector3();

await state.DoFileAsync("vector3_sample.lua");

// LuaObject属性とpartialキーワードを追加
[LuaObject]
public partial class LuaVector3
{
    Vector3 vector;

    // Lua側で使用されるメンバーにLuaMember属性を付加
    // 引数にはLuaでの名前を指定 (省略した場合はメンバーの名前が使用される)
    [LuaMember("x")]
    public float X
    {
        get => vector.X;
        set => vector = vector with { X = value };
    }

    [LuaMember("y")]
    public float Y
    {
        get => vector.Y;
        set => vector = vector with { Y = value };
    }

    [LuaMember("z")]
    public float Z
    {
        get => vector.Z;
        set => vector = vector with { Z = value };
    }

    // staticメソッドの場合は通常のLua関数として解釈される
    [LuaMember("create")]
    public static LuaVector3 Create(float x, float y, float z)
    {
        return new LuaVector3()
        {
            vector = new Vector3(x, y, z)
        };
    }

    // インスタンスメソッドの場合は暗黙的に自身のインスタンス(this)が1番目の引数として追加される
    // これはLuaではinstance:method()のような表記でアクセスできる
    [LuaMember("normalized")]
    public LuaVector3 Normalized()
    {
        return new LuaVector3()
        {
            vector = Vector3.Normalize(vector)
        };
    }
}
```

```lua
-- vector3_sample.lua

local v1 = Vector3.create(1, 2, 3)
-- 1  2  3
print(v1.x, v1.y, v1.z)

local v2 = v1:normalized()
-- 0.26726123690605164  0.5345224738121033  0.8017836809158325
print(v2.x, v2.y, v2.z)
```

`[LuaMember]`を付加するフィールド/プロパティの型、またはメソッドの引数や戻り値の型は`LuaValue`またはそれに変換が可能である必要があります。

ただし、戻り値には`void`, `Task/Task<T>`, `ValueTask/ValueTask<T>`, `UniTask/UniTask<T>`, `Awaitable/Awaitable<T>`を利用することも可能です。

利用可能でない型に対してはSource Generatorがコンパイルエラーを出力します。

### LuaMetamethod

`[LuaMetamethod]`属性を追加することで、C#のメソッドをLua側で使用されるメタメソッドとして設定することが可能です。

例として、先ほどの`LuaVector3`クラスに`__add`, `__sub`, `__tostring`のメタメソッドを追加したコードを示します。

```cs
[LuaObject]
public partial class LuaVector3
{
    // 上のコードで書かれた実装は省略

    [LuaMetamethod(LuaObjectMetamethod.Add)]
    public static LuaVector3 Add(LuaVector3 a, LuaVector3 b)
    {
        return new LuaVector3()
        {
            vector = a.vector + b.vector
        };
    }
    
    [LuaMetamethod(LuaObjectMetamethod.Sub)]
    public static LuaVector3 Sub(LuaVector3 a, LuaVector3 b)
    {
        return new LuaVector3()
        {
            vector = a.vector - b.vector
        };
    }

    [LuaMetamethod(LuaObjectMetamethod.ToString)]
    public override string ToString()
    {
        return vector.ToString();
    }
}
```

```lua
local v1 = Vector3.create(1, 1, 1)
local v2 = Vector3.create(2, 2, 2)

print(v1) -- <1, 1, 1>
print(v2) -- <2, 2, 2>

print(v1 + v2) -- <3, 3, 3>
print(v1 - v2) -- <-1, -1, -1>
```

> [!NOTE]
> `__index`, `__newindex`は`[LuaObject]`の生成コードに利用されるため、設定することはできません。

## モジュールの読み込み

Luaでは`require`関数を用いてモジュールを読み込むことができます。通常のLuaでは`package.searchers`の検索関数を用いてモジュールの管理を行いますが、Lua-CSharpでは代わりに`ILuaModuleLoader`がモジュール読み込みの機構として提供されています。

```cs
public interface ILuaModuleLoader
{
    bool Exists(string moduleName);
    ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default);
}
```

これを`LuaState.ModuleLoader`に設定することでモジュールの読み込み方法を変更することができます。デフォルトのLoaderにはluaファイルからモジュールをロードする`FileModuleLoader`が設定されています。

また、`CompositeModuleLoader.Create(loader1, loader2, ...)`を利用することで複数のLoaderを組み合わせたLoaderを作成できます。

```cs
state.ModuleLoader = CompositeModuleLoader.Create(
    new FileModuleLoader(),
    new CustomModuleLoader()
);
```

また、ロード済みのモジュールは通常のLua同様に`package.loaded`テーブルにキャッシュされます。これは`LuaState.LoadedModules`からアクセスすることが可能です。

## 例外処理

Luaスクリプトの解析エラーや実行時例外は`LuaException`を継承した例外をスローします。これをcatchすることでエラー時の処理を行うことができます。

```cs
try
{
    await state.DoFileAsync("filename.lua");
}
catch (LuaParseException)
{
    // 構文にエラーがあった際の処理
}
catch (LuaRuntimeException)
{
    // 実行時例外が発生した際の処理
}
```

## Lua.Unity

Lua-CSharpはUnityで利用することも可能です。(Mono/IL2CPPの両方で動作します)
また、Lua-CSharpをUnityと統合するための`Lua.Unity`拡張パッケージも提供されています。

### 要件

* Unity 2021.3 以上

### インストール

1. [NugetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)をインストールします。

2. `NuGet > Manage NuGet Packages`からNuGetウィンドウを開き、`LuaCSharp`パッケージを検索してインストールします。

3. `Window > Package Manager`からPackage Managerウィンドウを開き、`[+] > Add package from git URL`から以下のURLを入力します。
    ```
    https://github.com/nuskey8/Lua-CSharp.git?path=src/Lua.Unity/Assets/Lua.Unity
    ```

### LuaImporter / LuaAsset

Lua.Unityを導入することで、`.lua`拡張子のファイルを`LuaAsset`として扱えるようになります。

![img](docs/img-lua-importer.png)

これは通常の`TextAsset`のように利用することが可能です。

```cs
var asset = Resources.Load<LuaAsset>("example");
await state.DoStringAsync(asset.Text, ct);
```

### Resources(Addressables)ModuleLoader

また、内部でResourcesまたはAddressablesを利用する`ILuaModuleLoader`の実装が用意されています。

```cs
// モジュールの読み込みにResourcesを利用する
state.ModuleLoader = new ResourcesModuleLoader();

// モジュールの読み込みにAddressablesを利用する (Addressablesパッケージが必要)
state.ModuleLoader = new AddressablesModuleLoader();
```

## 互換性

Lua-CSharpは.NETとの統合を念頭に設計されているため、C実装とは互換性がない仕様がいくつか存在します。

### バイナリ

Lua-CSharpはLuaバイトコードをサポートしません(luacなどは使用できません)。実行可能なのはLuaソースコードのみです。

### 文字コード

Lua-CSharpで利用される文字コードはUTF-16です。通常Luaは1バイトで1文字を表すエンコーディングを前提としているため、文字列まわりの動作が大きく異なります。

例えば、以下のコードの出力結果は通常のLuaでは`15`ですが、Lua-CSharpでは`5`です。

```lua
local l = string.len("あいうえお")
print(l)
```

Stringライブラリの関数はすべて文字列をUTF-16として扱う実装に変更されていることに注意してください。

### ガベージコレクション

Lua-CSharpはC#で実装されているため.NETのGCに依存しています。そのため、メモリ管理に関する動作が通常のLuaとは異なります。

`collectgarbage()`は利用可能ですが、これは単に`GC.Collect()`の呼び出しです。引数の値は無視されます。また、弱参照テーブル(week tables)はサポートされません。

### moduleライブラリ

moduleライブラリは`require()`および`package.loaded`のみが利用でき、それ以外の関数は実装されていません。これはLua-CSharpは.NETに最適化された独自のモジュール読み込みの機能を有するためです。

詳細は[モジュールの読み込み](#モジュールの読み込み)の項目を参照してください。

### debugライブラリ

現在debugライブラリの実装は提供されていません。これはLua-CSharpの内部実装がC実装とは大きく異なり、同じAPIのライブラリを提供することが難しいためです。これについては、v1までに実装可能な一部のAPIのみの提供、または代替となるデバッグ機能を検討しています。

## ライセンス

このライブラリは[MITライセンス](LICENSE)の下で提供されています。
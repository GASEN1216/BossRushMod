# SpawnPositionPolicy

抽取 `Utilities/SpawnPositionHelper.cs` 中数组与列表两个真实多点选位方法执行。夹具只替身 `Vector3` 数学与 `SnapToGround` 落地（原样返回），因此验证安全距离、排序、回退、不重复及两个重载一致；不验证 Unity 物理、NavMesh 或地面高度。测试保留多点方法既有的三维距离语义。

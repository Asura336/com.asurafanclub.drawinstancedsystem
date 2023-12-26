# Draw Instanced System

`UnityEngine.Graphics.DrawMeshInstancedProcedural` 的包装器，旨在减少提交数据给 GPU 的频率。


## 工作流程

将`绘制实例调度器`（`Com.Rendering.InstancedMeshRenderDispatcher`）挂在空节点上并保存为预制体，每个调度器保存一对材质球和网格作为绘制实例的依据，以名称索引。运行期通过名称查找绘制特定种类实例。

绘制实例调度器不会自动加载，需要在初始化阶段主动实例化。

要绘制实例的物体上挂载`绘制实例符号`（`Com.Rendering.InstancedMeshRenderToken`），这里建议也做成预制体。绘制实例符号保存定向到的调度器、每批次实例的颜色、包围盒（本地空间）和每实例偏移量等信息。需要在脚本中引用这个组件详细设置。

任何绘制实例符号被实例化之前需要保证其索引的调度器已经被正确加载。


## 内存

核心组件使用 `Job System` 完成计算工作，这会占据非托管内存，运行期可以在合适的时机调用 `InstancedMeshRenderDispatcher.TrimExcess()` 释放多余的非托管内存。

内部调度器会在内容无变化（没有增删对象）后的一段时间内尝试让出内存（预设周期 150s）。


## 着色器

目前内建的着色器基于 Unity 内置渲染管线，支持常见纹理（漫反射、法线、金属度和粗糙度）。
脚本中会写入特别的缓冲区，后续适配的着色器也要有相同的缓冲区才能正确工作：
- `_Colors`：`StructuredBuffer<half4>`，rgba 颜色缓冲区，在片元着色器按实例化索引 `unity_InstanceID` 读取颜色。
- `_Matrices`：`StructuredBuffer<float4x4>`，世界空间变换矩阵缓冲区，按实例化索引 `unity_InstanceID` 读取缓冲区内容为每个实例写入 `unity_ObjectToWorld`。
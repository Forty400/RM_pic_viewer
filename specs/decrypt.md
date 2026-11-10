# Decrypt

对齐 [Petschko/RPG-Maker-MV-Decrypter](https://github.com/Petschko/RPG-Maker-MV-Decrypter) 的图片处理思路，并支持 Art_Decrypterator3000。

## Supported encrypted extensions

- `.rpgmvp` (RPG Maker MV images)
- `.png_` (RPG Maker MZ / common encrypted PNG)

## Headers

| Kind | Magic (first bytes) | Header size | XOR length |
|------|---------------------|-------------|------------|
| RPG Maker MV/MZ | `RPGMV...` | 16 | 16 |
| Art Encrypter (legacy) | `ART\0ENCRYPTER...` | 16 | 16 |
| Art Encrypter FREE VERSION | `ART\0ENCRYPTER100FREE\0VERSION\0\0\0\0` | 32 | 32 |

## Algorithm

### RPGMV / legacy Art

1. 去掉 16 字节假头。
2. 对剩余数据的**前 16 字节**做 XOR（密钥 16 字节）；其后原样。
3. 得到标准 PNG。

### Art Encrypter FREE VERSION（Art_Decrypterator3000）

1. 校验 32 字节固定头。
2. 从 `System.json` 读取 `encryptionKey`（32 位 hex 字符串）。
3. 密钥变换：`MD5(encryptionKey字符串)` → 16 字节，再追加其逆序副本 → **32 字节**密钥。
4. 去掉 32 字节假头后，对剩余数据的**前 32 字节** XOR；其后原样。

此格式**不能**仅靠 PNG 头推导完整密钥（XOR 覆盖 IHDR 宽高与部分 CRC）。

## Key / 无密钥还原

对 RPGMV / legacy Art 图片（对齐 Petschko Restore Images / No-Key）：

1. **优先**用 PNG 头前缀推导密钥，并校验 IHDR 尺寸合理。  
   避免误用上级目录里“别的游戏”的 `System.json`。
2. 若推导失败，再尝试 `System.json` → `encryptionKey`（向上查找 `data/` 或 `www/data/`）。

对 Art Encrypter FREE VERSION：必须能找到同游戏的 `System.json`。

## 双击打开

安装或设置中启用「关联双击打开」后，资源管理器双击 `.rpgmvp` / `.png_` 会启动本阅读器。

## Acceptance

- `tests/testImg/` 下 RPGMV 样例无需 `System.json` 即可解密为有效 PNG。
- Art Encrypter FREE VERSION（如 `birth1.rpgmvp`）在旁路 `data/System.json` 下可解密，且 `Bitmap`/`IHDR` 尺寸有效。
- 关联启用后，命令行 `RpgMakerImageViewer.exe "xxx.rpgmvp"` 与双击打开效果一致。

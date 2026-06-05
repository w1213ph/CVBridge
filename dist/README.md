# CV Bridge

CV Bridge 是一个轻量级 Windows 桌面工具，用 SSH 在 Windows 和远程 Linux 桌面之间传输剪贴板文本和文件。

它适合这些场景：

- 远程桌面、堡垒机、VPN 环境不能直接复制粘贴
- 只能 SSH 登录远程 Linux，但没有 root/管理员权限
- 想把本地 AI 生成的代码一键送到服务器剪贴板
- 想保留远端剪贴板历史，并支持简单文件上传/下载

## 功能

- Windows 图形界面，双击 `CVBridge.exe` 即可打开
- 一键发送本地文本到远程 Linux
- 一键获取远程 Linux 剪贴板文本
- 尝试直接写入远程图形桌面剪贴板，远程桌面里可以直接 `Ctrl+V`
- 远端历史记录，支持刷新、加载、复制
- 文件上传和下载，支持目录递归传输
- 内置实时日志面板和完整日志窗口
- 不保存 SSH 密码，只使用 OpenSSH 密钥
- 远端核心逻辑运行在用户目录，不需要 root 权限

## 系统要求

Windows 端：

- Windows 10/11
- Windows OpenSSH Client，需要有 `ssh.exe`、`scp.exe`、`ssh-keygen.exe`
- .NET Framework 4.x，Windows 通常已自带

Linux 端：

- SSH 服务可访问
- 用户可以登录自己的 home 目录
- `bash`、`mkdir`、`cat`、`grep`、`scp` 等常见命令
- 可选：`xclip` 或 `xsel`
- 可选：Python + `libX11`，用于在没有 `xclip/xsel` 时写入图形剪贴板

即使远程没有图形剪贴板工具，CV Bridge 也会把文本保存到：

```text
~/.server_clipboard_bridge/clipboard.txt
```

## 快速开始

### 1. 下载或构建

从 GitHub Release 下载 `CVBridge-release.zip`，解压后运行：

```text
CVBridge.exe
```

如果你要从源码构建，在 Windows PowerShell 中运行：

```powershell
.\scripts\build.ps1
```

构建结果会出现在：

```text
dist\CVBridge.exe
```

### 2. 准备 SSH 密钥

如果你还没有免密 SSH，可以运行：

```powershell
.\scripts\setup-ssh-key.ps1 -HostName 192.168.1.100 -UserName your_linux_user
```

第一次会要求输入 Linux 用户密码。完成后脚本会测试密钥登录。

自定义端口和密钥路径：

```powershell
.\scripts\setup-ssh-key.ps1 -HostName 192.168.1.100 -UserName your_linux_user -Port 22 -KeyPath "$env:USERPROFILE\.ssh\cvb_ed25519"
```

### 3. 配置软件

打开 `CVBridge.exe`，填写：

- `Host`：远程 Linux IP 或域名
- `User`：远程 Linux 用户名
- `Port`：SSH 端口，默认 `22`
- `Key`：Windows 私钥路径，例如 `%USERPROFILE%\.ssh\id_ed25519`
- `Remote Dir`：远端工作目录，默认 `.server_clipboard_bridge`

点击保存，然后点击测试连接。

也可以复制 `CVBridge.example.ini` 为 `CVBridge.ini`，放到 `CVBridge.exe` 同目录后手动编辑。

## 使用方式

文本传输：

- 在左侧输入文本，点击发送到服务器
- 成功后，远程图形桌面通常可以直接 `Ctrl+V`
- 点击获取服务器，可以把远端剪贴板/缓存文本拉回 Windows
- 右侧历史记录可以加载以前发送过的内容

文件传输：

- 选择本地文件或文件夹
- 填写远程路径
- 点击上传文件或下载文件
- 上传目录时勾选目录递归

日志：

- 主界面底部会显示实时日志
- 点击完整日志可以查看、复制、清空日志文件
- 日志文件保存在 `CVBridge.exe` 同目录的 `CVBridge.log`

## 隐私和安全

- CV Bridge 不保存 Linux 密码
- 私钥只保存在你的 Windows 用户目录
- 请不要把 `CVBridge.ini`、私钥、日志里的敏感内容提交到 GitHub
- 发送过的剪贴板文本会保存在远端工作目录的历史记录中
- 如果你传输密钥、Token、密码等敏感信息，使用后请清理远端历史

清理远端缓存：

```bash
rm -rf ~/.server_clipboard_bridge
```

## 项目结构

```text
CVBridge-GitHub/
  src/CVBridge/CVBridge.cs       # WinForms 源码
  scripts/build.ps1              # 编译 exe
  scripts/package.ps1            # 构建 release zip
  scripts/setup-ssh-key.ps1      # 生成并安装 SSH 密钥
  docs/architecture.md           # 工作原理
  docs/troubleshooting.md        # 常见问题
  CVBridge.example.ini           # 示例配置
```

## 打包发布

```powershell
.\scripts\package.ps1
```

输出：

```text
CVBridge-release.zip
```

仓库发布流程见 [PUBLISHING.md](PUBLISHING.md)。

## 常见问题

如果 SSH 测试失败，先在 PowerShell 中直接测试：

```powershell
ssh -i C:\Users\YOU\.ssh\id_ed25519 -o IdentitiesOnly=yes user@host
```

如果远程桌面里 `Ctrl+V` 没反应，查看：

```bash
cat ~/.server_clipboard_bridge/cvbridge_x11_clipboard.log
cat ~/.server_clipboard_bridge/clipboard.txt
```

更多排查见 [docs/troubleshooting.md](docs/troubleshooting.md)。

## License

MIT License. See [LICENSE](LICENSE).

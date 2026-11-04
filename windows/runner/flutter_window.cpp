#include "flutter_window.h"

#include <optional>
#include <algorithm>
#include <dwmapi.h>
#include "utils.h"

#include "flutter/generated_plugin_registrant.h"

FlutterWindow::FlutterWindow(const flutter::DartProject& project, bool translation)
    : project_(project), translation_(translation) {}

FlutterWindow::~FlutterWindow() {}

bool FlutterWindow::OnCreate() {
  if (!Win32Window::OnCreate()) {
    return false;
  }

  // 使用系统圆角与边缘阴影，标题栏由 Flutter 绘制。
  int corners = 2;
  DwmSetWindowAttribute(GetHandle(), 33, &corners, sizeof(corners));
  MARGINS margins = {1, 1, 1, 1};
  DwmExtendFrameIntoClientArea(GetHandle(), &margins);
  SetWindowPos(GetHandle(), nullptr, 0, 0, 0, 0,
               SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
  RECT frame = GetClientArea();

  // The size here must match the window dimensions to avoid unnecessary surface
  // creation / destruction in the startup path.
  flutter_controller_ = std::make_unique<flutter::FlutterViewController>(
      frame.right - frame.left, frame.bottom - frame.top, project_);
  // Ensure that basic setup of the controller was successful.
  if (!flutter_controller_->engine() || !flutter_controller_->view()) {
    return false;
  }
  RegisterPlugins(flutter_controller_->engine());
  SetChildContent(flutter_controller_->view()->GetNativeWindow());

  flutter_controller_->engine()->SetNextFrameCallback([&]() {
    const auto arguments = GetCommandLineArguments();
    if (std::find(arguments.begin(), arguments.end(), "--tray") == arguments.end()) {
      this->Show();
    }
  });

  // Flutter can complete the first frame before the "show window" callback is
  // registered. The following call ensures a frame is pending to ensure the
  // window is shown. It is a no-op if the first frame hasn't completed yet.
  flutter_controller_->ForceRedraw();

  return true;
}

void FlutterWindow::OnDestroy() {
  if (flutter_controller_) {
    flutter_controller_ = nullptr;
  }

  Win32Window::OnDestroy();
}

LRESULT
FlutterWindow::MessageHandler(HWND hwnd, UINT const message,
                              WPARAM const wparam,
                              LPARAM const lparam) noexcept {
  // 关闭时收起到托盘，退出由后台服务统一管理。
  if (message == WM_CLOSE) {
    ShowWindow(hwnd, SW_HIDE);
    return 0;
  }
  if (message == WM_NCCALCSIZE && wparam) return 0;
  if (message == WM_APP + 20) {
    SetProp(hwnd, L"WinToolDocked", reinterpret_cast<HANDLE>(wparam));
    return 0;
  }
  // 前台界面授权桥接进程恢复粘贴目标的焦点。
  if (message == WM_APP + 21) {
    AllowSetForegroundWindow(static_cast<DWORD>(wparam));
    return 0;
  }
  // 在持有鼠标捕获的界面线程中启动系统窗口拖动。
  if (message == WM_APP + 22) {
    ReleaseCapture();
    SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    return 0;
  }
  if (message == WM_SYSCOMMAND && GetProp(hwnd, L"WinToolDocked") &&
      (wparam & 0xFFF0) == SC_MAXIMIZE) return 0;
  if (message == WM_GETMINMAXINFO) {
    auto limits = reinterpret_cast<MINMAXINFO*>(lparam);
    const double scale = GetDpiForWindow(hwnd) / 96.0;
    limits->ptMinTrackSize.x = static_cast<LONG>(520 * scale);
    limits->ptMinTrackSize.y = GetProp(hwnd, L"WinToolDocked") ? 200
        : static_cast<LONG>((translation_ ? 500 : 420) * scale);
    return 0;
  }
  if (message == WM_NCHITTEST && !GetProp(hwnd, L"WinToolDocked")) {
    RECT bounds;
    GetWindowRect(hwnd, &bounds);
    const int x = static_cast<short>(LOWORD(lparam));
    const int y = static_cast<short>(HIWORD(lparam));
    const int grip = static_cast<int>(8 * GetDpiForWindow(hwnd) / 96.0);
    const bool left = x < bounds.left + grip, right = x >= bounds.right - grip;
    const bool top = y < bounds.top + grip, bottom = y >= bounds.bottom - grip;
    if (top && left) return HTTOPLEFT;
    if (top && right) return HTTOPRIGHT;
    if (bottom && left) return HTBOTTOMLEFT;
    if (bottom && right) return HTBOTTOMRIGHT;
    if (left) return HTLEFT;
    if (right) return HTRIGHT;
    if (top) return HTTOP;
    if (bottom) return HTBOTTOM;
  }
  // Give Flutter, including plugins, an opportunity to handle window messages.
  if (flutter_controller_) {
    std::optional<LRESULT> result =
        flutter_controller_->HandleTopLevelWindowProc(hwnd, message, wparam,
                                                      lparam);
    if (result) {
      return *result;
    }
  }

  switch (message) {
    case WM_FONTCHANGE:
      flutter_controller_->engine()->ReloadSystemFonts();
      break;
  }

  return Win32Window::MessageHandler(hwnd, message, wparam, lparam);
}

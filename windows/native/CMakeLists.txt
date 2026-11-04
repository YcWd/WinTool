# 原生服务随 Flutter runner 一起生成，flutter run 与正式打包共用同一路径。
set(NATIVE_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../..")
set(CSC "$ENV{WINDIR}/Microsoft.NET/Framework64/v4.0.30319/csc.exe")
set(NATIVE_SOURCES
  "${CMAKE_CURRENT_SOURCE_DIR}/BridgeHost.cs"
  "${CMAKE_CURRENT_SOURCE_DIR}/ClipboardBridge.cs"
  "${CMAKE_CURRENT_SOURCE_DIR}/Diagnostics.cs"
  "${NATIVE_ROOT}/NativeMethods.cs"
  "${NATIVE_ROOT}/HistoryStore.cs"
  "${NATIVE_ROOT}/AppSettings.cs"
  "${NATIVE_ROOT}/TranslationSettings.cs"
  "${NATIVE_ROOT}/DeepSeekTranslator.cs"
  "${NATIVE_ROOT}/TrayMenu.cs"
  "${NATIVE_ROOT}/FluentStyle.cs"
)
set(NATIVE_OUTPUT "${CMAKE_CURRENT_BINARY_DIR}/WinTool.Native.exe")
add_custom_command(OUTPUT "${NATIVE_OUTPUT}"
  COMMAND "${CSC}" /nologo /target:exe /platform:anycpu /optimize+ /codepage:65001
    "/out:${NATIVE_OUTPUT}" "/win32icon:${NATIVE_ROOT}/assets/clipboard.ico"
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll
    /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll
    /reference:System.Net.Http.dll /reference:System.Security.dll /reference:System.Web.Extensions.dll
    ${NATIVE_SOURCES}
  DEPENDS ${NATIVE_SOURCES} "${NATIVE_ROOT}/assets/clipboard.ico"
  VERBATIM
)
add_custom_target(wintool_native ALL DEPENDS "${NATIVE_OUTPUT}")
add_dependencies(${BINARY_NAME} wintool_native)
add_custom_command(TARGET wintool_native POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E make_directory "$<TARGET_FILE_DIR:${BINARY_NAME}>"
  COMMAND ${CMAKE_COMMAND} -E copy_if_different "${NATIVE_OUTPUT}" "$<TARGET_FILE_DIR:${BINARY_NAME}>/WinTool.Native.exe"
)

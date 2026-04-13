using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;

namespace Registry_Cleaner
{
    // CA1822 해결: 내부 데이터(필드)를 안 쓰면 static 클래스/메서드가 성능상 유리합니다.
    public static class Scanner
    {
        // 1. 시작 프로그램 스캔
        public static List<RegistryItem> ScanStartupItems()
        {
            var results = new List<RegistryItem>();
            string[] locations =
            [
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce"
            ];

            foreach (var location in locations)
            {
                CheckRegistry(Registry.CurrentUser, location, results);
                CheckRegistry(Registry.LocalMachine, location, results);
            }

            return results;
        }

        // 2. Unused Software 스캔 (Target 2)
        public static List<RegistryItem> ScanUnusedSoftware()
        {
            var results = new List<RegistryItem>();

            // 프로그램 정보가 담기는 핵심 경로들
            string[] value = [
                @"Software", // HKCU\Software
        @"SOFTWARE"  // HKLM\SOFTWARE
            ];
            string[] locations =
            value;

            // 절대 건드리면 안 되는 화이트리스트 (Microsoft, 시스템 드라이버 등)
            string[] whiteList = ["Microsoft", "Windows", "Classes", "Clients", "Policies", "RegisteredApplications", "WOW6432Node", "Intel", "AMD", "NVIDIA", "Realtek"];

            // 1. HKCU(현재 사용자) 검사
            CheckSoftwareRegistry(Registry.CurrentUser, "Software", whiteList, results);
            // 2. HKLM(시스템 전체) 검사
            CheckSoftwareRegistry(Registry.LocalMachine, "SOFTWARE", whiteList, results);

            return results;
        }

        // 3. MUI Cache 스캔 (Target 3)
        public static List<RegistryItem> ScanMuiCache()
        {
            var results = new List<RegistryItem>();
            // MUI Cache가 저장되는 전형적인 경로
            string muiPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(muiPath);
            if (key == null) return results;

            foreach (string valueName in key.GetValueNames())
            {
                // 이름 자체가 파일 경로인 경우가 많음 (예: "C:\App\test.exe.FriendlyAppName")
                if (string.IsNullOrEmpty(valueName) || valueName == "LangID") continue;

                // 경로 부분만 추출 (파일명 뒤에 .FriendlyAppName 등이 붙는 경우 처리)
                string potentialPath = valueName;
                if (valueName.Contains(".ApplicationCompany") || valueName.Contains(".FriendlyAppName"))
                {
                    potentialPath = valueName.Split('.')[0] + "." + valueName.Split('.')[1];
                    // 실제 경로 정제를 위해 확장자(.exe)까지만 자르는 로직이 필요할 수 있음
                    int exeIndex = valueName.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                    if (exeIndex != -1) potentialPath = valueName.Substring(0, exeIndex + 4);
                }

                if (!string.IsNullOrEmpty(potentialPath) && potentialPath.Contains('\\') && !File.Exists(potentialPath))
                {
                    results.Add(new RegistryItem
                    {
                        IsChecked = true,
                        Category = "MUI 캐시",
                        Name = "유령 실행 기록",
                        Data = potentialPath,
                        KeyPath = $@"HKCU\{muiPath}",
                        Reason = "실행 파일이 삭제된 과거 기록"
                    });
                }
            }
            return results;
        }

        // 4. File Extensions (잘못된 확장자 연결) 스캔 (Target 4)
        public static List<RegistryItem> ScanFileExtensions()
        {
            var results = new List<RegistryItem>();
            // HKCU\Software\Classes 아래의 확장자(.txt, .jpg 등)들 검사
            using RegistryKey? root = Registry.CurrentUser.OpenSubKey(@"Software\Classes");
            if (root == null) return results;

            foreach (string extName in root.GetSubKeyNames())
            {
                if (!extName.StartsWith('.')) continue; // 확장자 키만 타겟팅

                using RegistryKey? extKey = root.OpenSubKey($@"{extName}\shell\open\command");
                if (extKey == null) continue;

                object? val = extKey.GetValue(""); // 기본값 읽기
                if (val == null) continue;

                string commandPath = ParsePath(val.ToString() ?? "");
                if (!string.IsNullOrEmpty(commandPath) && !File.Exists(commandPath))
                {
                    results.Add(new RegistryItem
                    {
                        IsChecked = true,
                        Category = "확장자 연결",
                        Name = extName,
                        Data = commandPath,
                        KeyPath = $@"HKCU\Software\Classes\{extName}",
                        Reason = "연결된 프로그램이 존재하지 않음"
                    });
                }
            }
            return results;


        }


        private static void CheckSoftwareRegistry(RegistryKey root, string subKey, string[] whiteList, List<RegistryItem> results)
        {
            using RegistryKey? key = root.OpenSubKey(subKey);
            if (key == null) return;

            foreach (string subkeyName in key.GetSubKeyNames())
            {
                // 화이트리스트에 포함된 항목은 스킵
                if (whiteList.Any(w => subkeyName.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;

                // 해당 소프트웨어의 실제 설치 경로가 존재하는지 체크하는 로직
                // (보통 Software\제조사\프로그램명 구조이므로 한 단계 더 들어갑니다)
                using RegistryKey? softwareKey = key.OpenSubKey(subkeyName);
                if (softwareKey == null) continue;

                // 여기에 '이 소프트웨어가 실제로 존재하지 않는가?'를 판별하는 정밀 체크 로직이 들어갑니다.
                // 우선은 빈 키(Subkey가 없고 값도 없는 경우)나 경로가 끊긴 경우를 타겟팅합니다.
                if (softwareKey.SubKeyCount == 0 && softwareKey.ValueCount == 0)
                {
                    results.Add(new RegistryItem
                    {
                        IsChecked = true,
                        Category = "소프트웨어 잔상",
                        Name = subkeyName,
                        Data = "비어 있는 레지스트리 키",
                        KeyPath = $@"{root.Name}\{subKey}\{subkeyName}",
                        Reason = "프로그램 삭제 후 남겨진 빈 설정값"
                    });
                }
            }
        }

        // 5. 선택된 항목 실제 삭제 로직
        public static void DeleteRegistryItem(RegistryItem item)
        {
            try
            {
                if (item.KeyPath is null) return; // 기초 Null 방어

                RegistryKey root = item.KeyPath.StartsWith("HKEY_LOCAL_MACHINE") || item.KeyPath.StartsWith("HKLM")
                                   ? Registry.LocalMachine : Registry.CurrentUser;

                int slashIndex = item.KeyPath.IndexOf('\\');
                string subKeyPath = slashIndex != -1 ? item.KeyPath[(slashIndex + 1)..] : item.KeyPath;

                using RegistryKey? key = root.OpenSubKey(subKeyPath, true);
                if (key is null) return; // CS8602 해결

                if (item.Category == "소프트웨어 잔상" || item.Category == "확장자 연결")
                {
                    root.DeleteSubKeyTree(subKeyPath, false);
                }
                else if (item.Category == "MUI 캐시")
                {
                    string searchTarget = item.Data ?? string.Empty;
                    foreach (string valName in key.GetValueNames())
                    {
                        if (!string.IsNullOrEmpty(searchTarget) && valName.Contains(searchTarget, StringComparison.OrdinalIgnoreCase))
                        {
                            key.DeleteValue(valName);
                            return;
                        }
                    }
                }
                else
                {
                    // CS8604 해결: item.Name이 null인지 체크
                    if (item.Name is not null && key.GetValue(item.Name) != null)
                    {
                        key.DeleteValue(item.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"삭제 실패: {ex.Message}");
            }
        }

        private static void CheckRegistry(RegistryKey root, string subKey, List<RegistryItem> results)
        {
            using RegistryKey? key = root.OpenSubKey(subKey);
            if (key == null) return;

            foreach (string valueName in key.GetValueNames())
            {
                object? val = key.GetValue(valueName);
                if (val == null) continue;

                string rawData = val.ToString() ?? string.Empty;
                string cleanPath = ParsePath(rawData);

                if (!string.IsNullOrEmpty(cleanPath) && !File.Exists(cleanPath))
                {
                    results.Add(new RegistryItem
                    {
                        Category = "시작 프로그램",
                        Name = valueName,
                        Data = rawData,
                        KeyPath = $@"{root.Name}\{subKey}",
                        Reason = "파일이 존재하지 않음"
                    });
                }
            }
        }

        private static string ParsePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            // 환경 변수 치환 (%SystemRoot% 등)
            string expandedPath = Environment.ExpandEnvironmentVariables(path);

            if (expandedPath.StartsWith('\"'))
            {
                var parts = expandedPath.Split('\"');
                return parts.Length > 1 ? parts[1] : expandedPath;
            }

            return expandedPath.Split(' ')[0];
        }

    }
}
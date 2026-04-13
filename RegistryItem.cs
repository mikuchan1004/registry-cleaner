using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Registry_Cleaner
{
    public class RegistryItem
    {
        public bool IsChecked { get; set; } = true; // 삭제 선택 여부
        public string? Category { get; set; }        // "시작 프로그램", "MUI 캐시" 등
        public string? Name { get; set; }            // 레지스트리 값 이름
        public string? Data { get; set; }            // 파일 경로 등 실제 데이터
        public string? KeyPath { get; set; }         // 삭제를 위한 레지스트리 풀 경로
        public string? Reason { get; set; }          // "파일을 찾을 수 없음" 등
    }
}

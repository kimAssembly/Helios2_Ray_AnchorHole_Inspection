# Anchor Hole Workcell

LUCID Vision Labs **Helios2 Ray** 3D ToF 카메라의 실시간 포인트클라우드에서 평면 작업부를 찾고, 주변 평면보다 깊게 측정되는 국소 피크를 앙카 드릴 홀 후보로 검출해 XYZ 좌표를 출력하는 Windows 데스크톱 프로그램입니다.

## 사용 장비

이 프로젝트는 **LUCID Helios2 Ray HTR003S-001**을 기준으로 제작했습니다.

| 항목 | 내용 |
|---|---|
| 센서 | Sony DepthSense IMX556PLR ToF CMOS |
| 깊이 해상도 | 640 × 480 |
| 최대 프레임률 | 30 FPS |
| 조명 | 940 nm VCSEL, Class 1 |
| 공식 동작 거리 | 0.3–8.3 m |
| 인터페이스 | 1000BASE-T GigE, M12 X-coded |
| 방진·방수 | IP67 (적합한 IP67 케이블 사용 시) |
| 포인트 형식 | `Coord3D_ABCY16` — XYZ + intensity, 채널당 16-bit unsigned |

제품 사양과 운용 조건은 [LUCID Helios2 Ray 공식 제품 페이지](https://thinklucid.com/product/helios2-ray-outdoor-tof-ip67-3d-camera/)를 참고하세요.

> Helios2 Ray는 PoE를 지원하지 않습니다. 공식 사양상 GPIO를 통한 18–24 V 전원이 필요합니다.

## 주요 기능

- Arena SDK를 이용한 Helios2 Ray 검색 및 라이브 스트리밍
- 카메라가 링크로컬 `169.254.x.x`로 발견될 경우 `192.168.0.41/24`로 임시 Force-IP 복구
- 근거리부터 중거리까지 선택 가능한 Z 필터 프리셋
- 마우스 드래그 방식의 작업 ROI 지정
- 깊이에 따른 실시간 false-color heatmap
- 사용자 고정 깊이값 없이 현재 표면 노이즈에서 홀 임계값 자동 계산
- 홀의 화면 위치, 평면 대비 깊이, confidence 및 카메라 좌표계 XYZ(mm) 출력
- 3프레임 지속성 검사와 이동평균 기반 검출 안정화
- 홀 번호별 ROI 표면 잔차 프로파일과 자동 임계선/홀 피크 시각화

## 검출 알고리즘

1. 선택한 ROI와 Z 범위 밖의 포인트를 제거합니다.
2. RANSAC으로 ROI의 주 작업 평면을 추정합니다.
3. 평면 inlier의 RMSE와 MAD로 현재 프레임의 자동 깊이 임계값을 계산합니다.
4. 주변 여러 방향이 연속된 평면이면서 중심부만 깊은 국소 peak를 선별합니다.
5. 인접 peak를 하나의 홀 후보로 병합합니다.
6. 깊은 포인트 묶음의 중앙값을 사용해 단일 이상점의 영향을 줄입니다.
7. 동일 위치에서 3프레임 연속 확인된 후보만 표시하고 XYZ를 시간축으로 평활화합니다.

이 방식은 학습 모델이나 사전 템플릿 없이 평면상의 함몰부를 찾습니다. 따라서 단차, 물체 가장자리, 반사·흡광 재질 및 유효 깊이 포인트가 없는 검은 영역은 오검출 또는 미검출 원인이 될 수 있습니다.

## 프로젝트 구조

```text
AnchorHoleWorkcell/
├─ AnchorHoleWorkcell.slnx
├─ src/AnchorHoleWorkcell/
│  ├─ Camera/HeliosCamera.cs
│  ├─ Detection/HoleDetector.cs
│  ├─ Detection/TemporalHoleTracker.cs
│  └─ MainWindow.xaml(.cs)
└─ tests/AnchorHoleWorkcell.SelfTest/
```

`HeliosPose` 등 다른 로컬 프로젝트를 참조하지 않으며, 설치된 LUCID Arena SDK의 `ArenaNET_MP.dll`만 직접 참조합니다.

## 요구 환경

- Windows 10/11 64-bit
- Visual Studio 2022
- .NET 10 SDK
- LUCID Arena SDK 기본 설치 경로:
  `C:\Program Files\LUCID Vision Labs\Arena SDK`
- Helios2 Ray와 동일 서브넷에 연결된 GigE NIC

## 실행

1. ArenaView가 카메라 스트림을 사용 중이면 스트리밍을 정지하거나 ArenaView를 종료합니다.
2. `AnchorHoleWorkcell.slnx`를 Visual Studio에서 엽니다.
3. `AnchorHoleWorkcell`을 시작 프로젝트로 지정합니다.
4. x64 구성으로 실행합니다.
5. `LIVE START`를 누릅니다.
6. 작업 거리와 맞는 Z 범위를 선택합니다.
7. 홀 주변의 평면이 충분히 포함되도록 ROI를 드래그합니다.
8. `HOLE INSPECT`를 켭니다.

ROI는 홀만 타이트하게 잡지 말고 **평평한 면 70–90%와 홀**이 함께 들어오도록 잡는 것이 좋습니다. 최초 결과는 3프레임 검증 때문에 약 1초 뒤 표시됩니다.

## 기본 조정값

- 피크 병합 반경: `18 px`
- 평면 허용오차: `4 mm`
- 거친 콘크리트 표면: 평면 허용오차 `6–10 mm`부터 시험
- 한 홀이 여러 개로 분리됨: 병합 반경 증가
- 서로 다른 홀이 합쳐짐: 병합 반경 감소

## 빌드 및 테스트

```powershell
dotnet build .\AnchorHoleWorkcell.slnx -c Release
dotnet run --project .\tests\AnchorHoleWorkcell.SelfTest\AnchorHoleWorkcell.SelfTest.csproj -c Release
```

셀프테스트는 노이즈가 있는 합성 평면에 약 38 mm 깊이의 국소 함몰부를 만들고 홀 위치와 XYZ가 검출되는지 검사합니다.

## 주의사항

- 출력 XYZ는 **카메라 좌표계 기준 mm**입니다. 로봇 또는 작업 셀 좌표로 사용하려면 별도의 외부 캘리브레이션이 필요합니다.
- Force-IP는 현재 세션의 임시 주소 변경입니다. 영구 IP는 ArenaView 또는 LUCID IP Configuration Utility에서 설정하세요.
- 실제 앙카 홀 판정 성능은 표면 재질, 카메라 각도, 거리, 주변광 및 홀 직경/깊이에 영향을 받습니다.
- 본 프로젝트는 LUCID Vision Labs의 공식 소프트웨어가 아닌 별도 응용 프로그램입니다.

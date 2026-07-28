# HoloLens Medical Phantom AR Registration

Intel RealSense 뎁스 카메라와 연동하여 도출된 ICP(Iterative Closest Point) 변환 행렬을 수신하고, HoloLens 상에서 실제 환자(팬텀) 모델 위에 가상의 스킨 및 병변 3D 데이터를 실시간으로 정합(Overlay)하는 AR 클라이언트 어플리케이션입니다.

## 🌟 Key Features
* **실시간 AR 정합 (Real-time AR Overlay)**: 서버로부터 수신한 4x4 Transformation Matrix를 적용하여 현실 공간의 팬텀 위치에 3D 메쉬(Skin, Tumor)를 밀리미터(mm) 단위의 오차로 정확하게 렌더링합니다.
* **이종 좌표계 변환 처리**: Open3D(Right-Handed, Y-Down) 기반의 행렬을 Unity(Left-Handed, Y-Up) 환경에 맞게 축 반전 및 스케일(m/mm) 보정 로직을 구현하여 수학적 정합 정확도를 확보합니다.
* **RESTful API 통신**: UnityWebRequest를 활용한 비동기(Coroutine) 네트워크 통신으로 백엔드 API(FastAPI)와 연동하여 정합 프로세스를 제어합니다.
* **MRTK 기반 직관적 UI**: Mixed Reality Toolkit(MRTK)을 활용하여 사용자가 시야 내에서 정합을 트리거하고, 모델의 투명도 등을 제어할 수 있는 슬라이더 UI를 제공합니다.

## 🛠 Tech Stack
**Hardware**
* Microsoft HoloLens 1세대
* (Server) Intel RealSense Depth Camera D435f

**Client (AR Application)**
* Unity 2019.4.41f2
* C# (.NET)
* Mixed Reality Toolkit (MRTK) v2
* Universal Windows Platform (UWP)

## 🚀 Getting Started

### Prerequisites
* Unity 2019.4.41f2 및 UWP Build Support 모듈 설치
* Visual Studio 2019 및 UWP 개발 워크로드 구성
* 백엔드 API 서버(`RealSense-Face-ICP-WebAPI`) 실행 상태

### Installation & Execution

1. **Clone the repository**
   ```bash
   git clone [https://github.com/sheepjun96/RealSense-HoloLens-AR-Viewer.git](https://github.com/sheepjun96/RealSense-HoloLens-AR-Viewer.git)

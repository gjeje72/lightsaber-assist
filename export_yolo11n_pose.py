from ultralytics import YOLO

YOLO("yolo11n-pose.pt").export(
    format="onnx",
    imgsz=640
)
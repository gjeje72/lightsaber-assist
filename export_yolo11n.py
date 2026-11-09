from ultralytics import YOLO

YOLO("yolo11n.pt").export(
    format="onnx",
    imgsz=640
)
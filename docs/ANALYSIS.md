## Analyse vidéo

### Modèle YOLO11-pose (ONNX)

Placer `yolo11n-pose.onnx` dans `lightsaber-assist.WinUI/Assets/Models/`.

Export depuis Ultralytics :

```
pip install ultralytics
yolo export model=yolo11n-pose.pt format=onnx imgsz=640
```

Sortie attendue : `[1, 56, N]` (cx, cy, w, h, score, 17 x (x, y, confiance)).

### FFmpeg

Placer `ffmpeg.exe` et `ffprobe.exe` (et leurs DLL éventuelles) dans `lightsaber-assist.WinUI/Assets/ffmpeg/`,
ou les rendre disponibles dans le `PATH`.

### Données

- Cache d'analyse et annotations : `%LocalAppData%/lightsaber-assist/cache`.
- Export JSON : frames, combattants, boîtes, 17 keypoints, centre de gravité, distances px/normalisée, annotations.
- Centre de gravité : milieu des hanches (repli : épaules, puis centre de la boîte).
- Distance normalisée : distance en pixels / hauteur moyenne des boîtes.

### Sabres

- Détection (OpenCvSharp) : pixels très lumineux (V >= 230) dans la zone des combattants, morphologie, HoughLinesP, fusion des segments colinéaires.
- Association : segment le plus proche d'un poignet (ou du milieu des deux poignets si prise à deux mains), un sabre par combattant. Hilt = extrémité côté poignée.
- Limites : fonds/reflets très lumineux, sabre éteint, occultation. Seuils dans BrightSegmentDetector.
- Export JSON : champ Saber (Hilt, Tip) par combattant.

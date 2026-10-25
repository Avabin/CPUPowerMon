import QtQuick
import org.kde.kirigami as Kirigami

Canvas {
    id: chart

    property var values: []
    property bool active: true

    implicitWidth: Kirigami.Units.gridUnit * 3
    implicitHeight: Kirigami.Units.gridUnit * 1.2

    onValuesChanged: requestPaint()
    onActiveChanged: requestPaint()
    onWidthChanged: requestPaint()
    onHeightChanged: requestPaint()

    onPaint: {
        var ctx = getContext("2d");
        var w = chart.width;
        var h = chart.height;
        ctx.clearRect(0, 0, w, h);

        if (!values || values.length < 2 || w <= 0 || h <= 0) {
            return;
        }

        var minimum = values[0];
        var maximum = values[0];
        for (var i = 1; i < values.length; i++) {
            minimum = Math.min(minimum, values[i]);
            maximum = Math.max(maximum, values[i]);
        }

        var range = maximum - minimum;
        if (range < 0.1) {
            range = 0.1;
        }

        var padding = Math.max(1, Kirigami.Units.smallSpacing / 2);
        var stepX = (w - padding * 2) / (values.length - 1);
        var lineColor = active ? Kirigami.Theme.highlightColor : Kirigami.Theme.disabledTextColor;

        function pointX(index) {
            return padding + index * stepX;
        }

        function pointY(value) {
            return h - padding - ((value - minimum) / range) * (h - padding * 2);
        }

        ctx.beginPath();
        ctx.moveTo(pointX(0), h - padding);
        for (var j = 0; j < values.length; j++) {
            ctx.lineTo(pointX(j), pointY(values[j]));
        }
        ctx.lineTo(pointX(values.length - 1), h - padding);
        ctx.closePath();
        ctx.fillStyle = Qt.rgba(lineColor.r, lineColor.g, lineColor.b, active ? 0.18 : 0.08);
        ctx.fill();

        ctx.beginPath();
        for (var k = 0; k < values.length; k++) {
            if (k === 0) {
                ctx.moveTo(pointX(k), pointY(values[k]));
            } else {
                ctx.lineTo(pointX(k), pointY(values[k]));
            }
        }
        ctx.lineWidth = Math.max(1, Kirigami.Units.gridUnit / 14);
        ctx.lineJoin = "round";
        ctx.lineCap = "round";
        ctx.strokeStyle = lineColor;
        ctx.stroke();
    }
}

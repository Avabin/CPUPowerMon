import QtQuick
import org.kde.plasma.components as PlasmaComponents3
import org.kde.kirigami as Kirigami

Item {
    id: chart

    property var values: []
    property bool active: true
    readonly property int sampleSlots: 60
    readonly property real labelWidth: Kirigami.Units.gridUnit * 3.2
    readonly property real plotLeft: labelWidth + Kirigami.Units.smallSpacing
    readonly property real plotRight: width - Kirigami.Units.smallSpacing
    readonly property real plotTop: Kirigami.Units.smallSpacing * 3
    readonly property real plotBottom: height - Kirigami.Units.smallSpacing * 3
    readonly property real plotWidth: Math.max(0, plotRight - plotLeft)
    readonly property real plotHeight: Math.max(0, plotBottom - plotTop)
    readonly property real axisMax: computeAxisMax()

    implicitWidth: Kirigami.Units.gridUnit * 12
    implicitHeight: Kirigami.Units.gridUnit * 6

    onValuesChanged: canvas.requestPaint()
    onActiveChanged: canvas.requestPaint()

    function computeAxisMax() {
        var maximum = 0;
        for (var i = 0; i < values.length; i++) {
            if (isFinite(values[i])) {
                maximum = Math.max(maximum, values[i]);
            }
        }
        if (!isFinite(maximum) || maximum <= 5) {
            return 5;
        }
        var magnitude = Math.pow(10, Math.floor(Math.log(maximum) / Math.LN10));
        var step = magnitude / 2;
        return Math.ceil(maximum / step) * step;
    }

    function formatAxisValue(value) {
        if (Math.abs(value - Math.round(value)) < 0.05) {
            return Math.round(value) + " W";
        }
        if (value < 10) {
            return value.toFixed(1) + " W";
        }
        return value.toFixed(0) + " W";
    }

    Canvas {
        id: canvas
        anchors.fill: parent
        antialiasing: true

        onWidthChanged: requestPaint()
        onHeightChanged: requestPaint()

        onPaint: {
            var ctx = getContext("2d");
            ctx.clearRect(0, 0, width, height);

            if (chart.plotWidth <= 0 || chart.plotHeight <= 0) {
                return;
            }

            var textColor = Kirigami.Theme.textColor;
            var gridColor = Qt.rgba(textColor.r, textColor.g, textColor.b, 0.14);
            var lineColor = chart.active ? Kirigami.Theme.highlightColor : Kirigami.Theme.disabledTextColor;

            ctx.lineWidth = 1;
            ctx.strokeStyle = gridColor;
            for (var tick = 0; tick < 5; tick++) {
                var gridY = chart.plotTop + tick * chart.plotHeight / 4;
                ctx.beginPath();
                ctx.moveTo(chart.plotLeft, gridY);
                ctx.lineTo(chart.plotRight, gridY);
                ctx.stroke();
            }

            if (!chart.values || chart.values.length < 2) {
                return;
            }

            var count = Math.min(chart.values.length, chart.sampleSlots);
            var offset = chart.values.length - count;
            var stepX = chart.plotWidth / Math.max(1, count - 1);

            function pointX(index) {
                return chart.plotLeft + index * stepX;
            }

            function pointY(value) {
                var ratio = Math.max(0, Math.min(1, value / chart.axisMax));
                return chart.plotBottom - ratio * chart.plotHeight;
            }

            ctx.beginPath();
            ctx.moveTo(pointX(0), chart.plotBottom);
            for (var i = 0; i < count; i++) {
                ctx.lineTo(pointX(i), pointY(chart.values[offset + i]));
            }
            ctx.lineTo(pointX(count - 1), chart.plotBottom);
            ctx.closePath();
            ctx.fillStyle = Qt.rgba(lineColor.r, lineColor.g, lineColor.b, chart.active ? 0.20 : 0.08);
            ctx.fill();

            ctx.beginPath();
            for (var j = 0; j < count; j++) {
                var x = pointX(j);
                var y = pointY(chart.values[offset + j]);
                if (j === 0) {
                    ctx.moveTo(x, y);
                } else {
                    ctx.lineTo(x, y);
                }
            }
            ctx.strokeStyle = lineColor;
            ctx.lineWidth = Math.max(2, Kirigami.Units.gridUnit / 10);
            ctx.lineJoin = "round";
            ctx.lineCap = "round";
            ctx.stroke();

            var lastX = pointX(count - 1);
            var lastY = pointY(chart.values[offset + count - 1]);
            ctx.beginPath();
            ctx.arc(lastX, lastY, Math.max(2, Kirigami.Units.gridUnit / 10), 0, Math.PI * 2);
            ctx.fillStyle = lineColor;
            ctx.fill();
        }
    }

    Repeater {
        model: 5

        Item {
            id: tickItem
            width: chart.labelWidth
            height: tickLabel.implicitHeight
            y: chart.plotTop + index * chart.plotHeight / 4 - height / 2

            PlasmaComponents3.Label {
                id: tickLabel
                anchors.fill: parent
                horizontalAlignment: Text.AlignRight
                verticalAlignment: Text.AlignVCenter
                text: chart.formatAxisValue(chart.axisMax * (4 - index) / 4)
                opacity: 0.55
                font.pointSize: Kirigami.Theme.smallFont.pointSize
            }
        }
    }
}

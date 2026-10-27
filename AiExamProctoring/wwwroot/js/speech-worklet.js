class ExamPcm extends AudioWorkletProcessor {
    constructor() { super(); this.samples = []; this.sum = 0; this.count = 0; this.phase = 0; }
    process(inputs) {
        const input = inputs[0]?.[0];
        if (!input) return true;
        for (const value of input) {
            this.sum += value; this.count++; this.phase += 16000;
            if (this.phase >= sampleRate) {
                this.phase -= sampleRate;
                this.samples.push(Math.max(-32768, Math.min(32767, Math.round(this.sum / this.count * 32767))));
                this.sum = 0; this.count = 0;
                if (this.samples.length === 8000) {
                    const pcm = new Int16Array(this.samples); this.samples = [];
                    this.port.postMessage(pcm.buffer, [pcm.buffer]);
                }
            }
        }
        return true;
    }
}
registerProcessor('exam-pcm', ExamPcm);

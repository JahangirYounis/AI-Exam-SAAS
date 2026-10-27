(() => {
  document.querySelectorAll('.password-toggle').forEach(button => {
    button.addEventListener('click', () => {
      const input = button.parentElement?.querySelector('input');
      if (!input) return;
      const show = input.type === 'password';
      input.type = show ? 'text' : 'password';
      button.setAttribute('aria-label', show ? 'Hide password' : 'Show password');
      const icon = button.querySelector('i');
      if (icon) icon.className = show ? 'bi bi-eye-slash' : 'bi bi-eye';
    });
  });

  const video = document.getElementById('cameraPreview');
  const canvas = document.getElementById('photoCanvas');
  const image = document.getElementById('capturedPreview');
  const placeholder = document.getElementById('cameraPlaceholder');
  const start = document.getElementById('startCamera');
  const capture = document.getElementById('capturePhoto');
  const retake = document.getElementById('retakePhoto');
  const hidden = document.getElementById('capturedEnrollmentPhotos');
  const countLabel = document.getElementById('captureCount');
  if (!video || !canvas || !image || !start || !capture || !retake || !hidden) return;

  let stream = null;
  let photos = [];
  try { photos = JSON.parse(hidden.value || '[]'); if (!Array.isArray(photos) || photos.length > 10 || photos.some(p => typeof p !== 'string' || !p.startsWith('data:image/'))) photos = []; } catch { photos = []; }
  const form = hidden.closest('form');
  const uploads = form?.querySelector('input[type=file]');
  const maximum = () => form?.querySelector('input[name="Input.AccountType"]:checked')?.value === 'Teacher' ? 1 : 10;
  const minimum = () => maximum() === 1 ? 1 : 5;
  const total = () => photos.length + (uploads?.files.length || 0);
  const thumbnails = document.createElement('div');
  thumbnails.className = 'photo-thumbnails';
  thumbnails.setAttribute('aria-label', 'Selected photos');
  document.getElementById('cameraBox')?.parentElement?.append(thumbnails);
  let previewUrls = [];
  let lastCapture = -2000;
  const refresh = () => {
    hidden.value = JSON.stringify(photos);
    capture.disabled = total() >= maximum();
    retake.classList.toggle('d-none', total() === 0);
    previewUrls.forEach(url => URL.revokeObjectURL(url));
    previewUrls = [...(uploads?.files || [])].map(file => URL.createObjectURL(file));
    const sources = [...photos, ...previewUrls];
    thumbnails.replaceChildren();
    sources.forEach((src, index) => {
      const figure = document.createElement('figure');
      const photo = document.createElement('img'); photo.src = src; photo.alt = `Selected photo ${index + 1}`;
      const label = document.createElement('figcaption'); label.textContent = `Photo ${index + 1}`;
      figure.append(photo, label); thumbnails.append(figure);
      const remove = document.createElement('button');
      remove.type = 'button'; remove.className = 'btn btn-sm btn-outline-danger mt-1';
      remove.textContent = 'Remove'; remove.setAttribute('aria-label', `Remove photo ${index + 1}`);
      remove.onclick = () => {
        if (index < photos.length) photos.splice(index, 1);
        else {
          const remaining = new DataTransfer();
          [...uploads.files].forEach((file, i) => { if (i !== index - photos.length) remaining.items.add(file); });
          uploads.files = remaining.files;
        }
        refresh();
      };
      figure.append(remove);
    });
    if (!stream && sources.length) {
      image.src = sources.at(-1); image.classList.remove('d-none');
      video.classList.add('d-none'); placeholder?.classList.add('d-none');
    } else if (!stream) {
      image.classList.add('d-none'); video.classList.add('d-none'); placeholder?.classList.remove('d-none');
    }
    const help = document.getElementById('photoHelp');
    if (help) help.textContent = maximum() === 1 ? 'Add one profile photo. Teacher accounts need administrator approval before sign-in.' : 'Add 5 to 10 different photos of yourself. Keep one clear face visible in good lighting.';
    if (countLabel) countLabel.textContent = `${total()} / ${maximum()} photos · ${total() >= minimum() && total() <= maximum() ? 'Ready to submit' : 'Add clear photos'}`;
  };
  const stop = () => {
    if (!stream) return;
    stream.getTracks().forEach(track => track.stop());
    stream = null;
  };

  start.addEventListener('click', async () => {
    if (total() >= maximum()) { countLabel.textContent = 'Photos are ready. Clear all photos if you want to replace them.'; return; }
    stop();
    start.disabled = true;
    try {
      stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'user', width: { ideal: 720 }, height: { ideal: 720 } }, audio: false });
      video.srcObject = stream;
      await video.play();
      video.classList.remove('d-none');
      image.classList.add('d-none');
      placeholder?.classList.add('d-none');
      capture.classList.remove('d-none');
      retake.classList.toggle('d-none', total() === 0);
      start.classList.add('d-none');
    } catch {
      alert('Camera access was not available. Please allow camera permission or upload a recent clear photo.');
    } finally { start.disabled = false; }
  });

  capture.addEventListener('click', () => {
    if (!stream || video.readyState < 2 || !video.videoWidth || total() >= maximum()) return;
    if (performance.now() - lastCapture < 1500) return;
    lastCapture = performance.now();
    // Preserve the camera's field of view and actual detail instead of cropping
    // and enlarging a low-resolution square before presentation analysis.
    const scale = Math.min(1, 1280 / video.videoWidth);
    canvas.width = Math.round(video.videoWidth * scale);
    canvas.height = Math.round(video.videoHeight * scale);
    const ctx = canvas.getContext('2d');
    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
    const data = canvas.toDataURL('image/jpeg', 0.95);
    photos.push(data);
    hidden.value = JSON.stringify(photos);
    image.src = data;
    image.classList.remove('d-none');
    window.setTimeout(() => { if (stream) image.classList.add('d-none'); }, 650);
    retake.classList.remove('d-none');
    refresh();
    if (total() >= maximum()) { stop(); start.classList.remove('d-none'); refresh(); }
  });

  retake.addEventListener('click', () => {
    photos = [];
    hidden.value = '';
    image.classList.add('d-none');
    video.classList.remove('d-none');
    placeholder?.classList.add('d-none');
    capture.classList.remove('d-none');
    capture.disabled = false;
    capture.innerHTML = '<i class="bi bi-camera me-2"></i>Capture next photo';
    if (countLabel) countLabel.textContent = '0 photos captured';
    retake.classList.add('d-none');
    if (uploads) uploads.value = '';
    start.classList.remove('d-none');
    refresh();
  });

  uploads?.addEventListener('change', () => {
    if (total() > maximum() || [...uploads.files].some(f => !['image/jpeg', 'image/png'].includes(f.type) || f.size < 8000 || f.size > 5 * 1024 * 1024)) {
      uploads.value = '';
      alert(`Choose at most ${maximum() - photos.length} JPG/PNG photos, 8 KB–5 MB each.`);
    }
    refresh();
  });
  form?.querySelectorAll('input[name="Input.AccountType"]').forEach(role => role.addEventListener('change', () => { stop(); retake.click(); }));
  form?.addEventListener('submit', event => {
    const consent = document.getElementById('Input_AcceptMonitoringNotice');
    if (consent && !consent.checked) {
      event.preventDefault(); consent.reportValidity(); return;
    }
    if (total() < minimum() || total() > maximum()) {
      event.preventDefault();
      if (countLabel) countLabel.textContent = `Add ${minimum()}–${maximum()} photos before continuing.`;
      countLabel?.scrollIntoView({ block: 'center' });
    }
  });
  document.getElementById('Input_AcceptMonitoringNotice')?.addEventListener('change', event => {
    if (event.target.checked) {
      const error = form.querySelector('[data-valmsg-for="Input.AcceptMonitoringNotice"]');
      if (error) { error.textContent = ''; error.classList.remove('field-validation-error'); }
    }
  });
  refresh();
  window.addEventListener('pagehide', stop);

  window.addEventListener('beforeunload', stop);
})();

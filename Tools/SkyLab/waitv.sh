# variants.txt 마지막 줄 그림이 이 스크립트를 부른 뒤 새로 저장될 때까지 기다린 뒤 한 장으로 모은다. 인자: 시트 이름, 열, 배율
last=$(grep -v '^#' variants.txt | grep . | tail -1 | awk '{print $1}')
start=${WAIT_FROM:-$(date +%s)}
for i in $(seq 1 120); do
  if [ -f out/$last.png ] && [ $(stat -c %Y out/$last.png) -ge $start ]; then break; fi
  sleep 2
done
sleep 1
python sheet.py "$1" "${2:-4}" "${3:-0.8}"
